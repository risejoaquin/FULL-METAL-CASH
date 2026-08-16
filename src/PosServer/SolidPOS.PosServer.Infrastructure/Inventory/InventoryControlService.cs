using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Infrastructure.Inventory;

public sealed class InventoryControlService : IInventoryControlService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> OfflineBehaviors = new HashSet<string>(StringComparer.Ordinal)
    {
        "allow_and_reconcile",
        "warn_and_reconcile",
        "block_when_online"
    };

    private readonly ITenantContext _tenantContext;
    private readonly IInventoryControlRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<InventoryControlService> _logger;

    public InventoryControlService(
        ITenantContext tenantContext,
        IInventoryControlRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<InventoryControlService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<InventoryPolicyResponse?> GetPolicyAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        if (!TryResolveReadableStore(storeId, out Guid? resolvedStoreId))
        {
            return null;
        }

        return await _repository.GetPolicyAsync(_tenantContext.TenantId.Value, resolvedStoreId, cancellationToken);
    }

    public async Task<InventoryPolicyResponse?> UpsertPolicyAsync(UpdateInventoryPolicyRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        string behavior = request.OfflineSaleBehavior.Trim().ToLowerInvariant();
        if (!OfflineBehaviors.Contains(behavior))
        {
            _logger.LogWarning("Inventory policy rejected because offline behavior {OfflineBehavior} is invalid", request.OfflineSaleBehavior);
            return null;
        }

        if (_tenantContext.TerminalId.HasValue)
        {
            return null;
        }

        InventoryPolicyResponse? policy = await _repository.UpsertPolicyAsync(_tenantContext.TenantId.Value, request with { OfflineSaleBehavior = behavior }, cancellationToken);
        if (policy is not null)
        {
            await WriteAuditAsync(policy.TenantId, "inventory.policy.updated", "inventory_policy", policy.StoreId ?? policy.TenantId, policy, cancellationToken);
        }

        return policy;
    }

    public async Task<InventoryCountResponse?> CreateCountAsync(CreateInventoryCountRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !TryResolveWriteStore(request.StoreId, out Guid storeId))
        {
            return null;
        }

        if (request.LocalCountId == Guid.Empty || request.CreatedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Lines.Count == 0)
        {
            return null;
        }

        if (request.Lines.Any(x => x.ProductId == Guid.Empty || x.UnitId == Guid.Empty || !decimal.TryParse(x.CountedQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal counted) || counted < 0))
        {
            return null;
        }

        InventoryCountResponse? count = await _repository.CreateCountAsync(_tenantContext.TenantId.Value, storeId, _tenantContext.TerminalId, request with { StoreId = storeId, Reason = request.Reason.Trim() }, cancellationToken);
        if (count is not null)
        {
            await WriteAuditAsync(count.TenantId, "inventory.count.completed", "inventory_count", count.Id, count, cancellationToken);
        }

        return count;
    }

    public async Task<IReadOnlyCollection<InventoryCountListItemResponse>?> ListCountsAsync(InventoryControlFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !TryResolveReadableStore(filters.StoreId, out Guid? storeId))
        {
            return null;
        }

        return await _repository.ListCountsAsync(_tenantContext.TenantId.Value, filters with { StoreId = storeId, Limit = NormalizeLimit(filters.Limit) }, cancellationToken);
    }

    public async Task<InventoryTransferResponse?> CreateTransferAsync(CreateInventoryTransferRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || _tenantContext.TerminalId.HasValue)
        {
            return null;
        }

        if (request.LocalTransferId == Guid.Empty || request.FromStoreId == Guid.Empty || request.ToStoreId == Guid.Empty || request.FromStoreId == request.ToStoreId || request.CreatedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Lines.Count == 0)
        {
            return null;
        }

        if (request.Lines.Any(x => x.ProductId == Guid.Empty || x.UnitId == Guid.Empty || !decimal.TryParse(x.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity) || quantity <= 0))
        {
            return null;
        }

        InventoryTransferResponse? transfer = await _repository.CreateTransferAsync(_tenantContext.TenantId.Value, _tenantContext.TerminalId, request with { Reason = request.Reason.Trim() }, cancellationToken);
        if (transfer is not null)
        {
            await WriteAuditAsync(transfer.TenantId, "inventory.transfer.completed", "inventory_transfer", transfer.Id, transfer, cancellationToken);
        }

        return transfer;
    }

    public async Task<IReadOnlyCollection<InventoryTransferListItemResponse>?> ListTransfersAsync(InventoryControlFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !TryResolveReadableStore(filters.StoreId, out Guid? storeId))
        {
            return null;
        }

        return await _repository.ListTransfersAsync(_tenantContext.TenantId.Value, filters with { StoreId = storeId, Limit = NormalizeLimit(filters.Limit) }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<InventoryLowStockItemResponse>?> GetLowStockAsync(Guid? storeId, int limit, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !TryResolveReadableStore(storeId, out Guid? resolvedStoreId))
        {
            return null;
        }

        return await _repository.GetLowStockAsync(_tenantContext.TenantId.Value, resolvedStoreId, NormalizeLimit(limit), cancellationToken);
    }

    private bool TryResolveReadableStore(Guid? requestStoreId, out Guid? storeId)
    {
        storeId = requestStoreId;
        if (_tenantContext.TerminalId.HasValue)
        {
            if (!_tenantContext.StoreId.HasValue || (requestStoreId.HasValue && requestStoreId.Value != _tenantContext.StoreId.Value))
            {
                return false;
            }

            storeId = _tenantContext.StoreId.Value;
        }

        return true;
    }

    private bool TryResolveWriteStore(Guid? requestStoreId, out Guid storeId)
    {
        storeId = Guid.Empty;
        if (_tenantContext.TerminalId.HasValue)
        {
            if (!_tenantContext.StoreId.HasValue || (requestStoreId.HasValue && requestStoreId.Value != _tenantContext.StoreId.Value))
            {
                return false;
            }

            storeId = _tenantContext.StoreId.Value;
            return true;
        }

        if (!requestStoreId.HasValue || requestStoreId.Value == Guid.Empty)
        {
            return false;
        }

        storeId = requestStoreId.Value;
        return true;
    }

    private static int NormalizeLimit(int limit) => limit <= 0 ? 50 : Math.Min(limit, 200);

    private Task WriteAuditAsync<T>(Guid tenantId, string action, string entityType, Guid entityId, T afterData, CancellationToken cancellationToken)
    {
        return _auditEventWriter.AppendAsync(
            tenantId,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.SerializeToElement(afterData, JsonOptions),
            cancellationToken);
    }
}
