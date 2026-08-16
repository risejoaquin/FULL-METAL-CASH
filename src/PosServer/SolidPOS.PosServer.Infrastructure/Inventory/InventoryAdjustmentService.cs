using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Infrastructure.Inventory;

public sealed class InventoryAdjustmentService : IInventoryAdjustmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlySet<string> AllowedAdjustmentTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "stock_in",
        "stock_out",
        "waste",
        "correction"
    };

    private readonly ITenantContext _tenantContext;
    private readonly IInventoryAdjustmentRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<InventoryAdjustmentService> _logger;

    public InventoryAdjustmentService(
        ITenantContext tenantContext,
        IInventoryAdjustmentRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<InventoryAdjustmentService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<InventoryAdjustmentResponse?> CreateAsync(CreateInventoryAdjustmentRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Inventory adjustment rejected because tenant context is missing");
            return null;
        }

        if (!TryResolveStore(request.StoreId, out Guid storeId))
        {
            _logger.LogWarning(
                "Inventory adjustment {LocalAdjustmentId} rejected because store context is invalid for tenant {TenantId}",
                request.LocalAdjustmentId,
                _tenantContext.TenantId.Value);
            return null;
        }

        if (request.LocalAdjustmentId == Guid.Empty ||
            request.CreatedByUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) ||
            string.IsNullOrWhiteSpace(request.AdjustmentType) ||
            request.Lines is null ||
            request.Lines.Count == 0)
        {
            _logger.LogWarning(
                "Inventory adjustment {LocalAdjustmentId} rejected by required field validation for tenant {TenantId}",
                request.LocalAdjustmentId,
                _tenantContext.TenantId.Value);
            return null;
        }

        string adjustmentType = request.AdjustmentType.Trim();
        if (!AllowedAdjustmentTypes.Contains(adjustmentType))
        {
            _logger.LogWarning(
                "Inventory adjustment {LocalAdjustmentId} rejected because adjustment type {AdjustmentType} is invalid for tenant {TenantId}",
                request.LocalAdjustmentId,
                adjustmentType,
                _tenantContext.TenantId.Value);
            return null;
        }

        if (!LinesAreValid(request, adjustmentType))
        {
            _logger.LogWarning(
                "Inventory adjustment {LocalAdjustmentId} rejected by line validation for tenant {TenantId}",
                request.LocalAdjustmentId,
                _tenantContext.TenantId.Value);
            return null;
        }

        CreateInventoryAdjustmentRequest normalizedRequest = request with
        {
            StoreId = storeId,
            AdjustmentType = adjustmentType,
            Reason = request.Reason.Trim()
        };

        InventoryAdjustmentResponse? adjustment = await _repository.CreateAsync(
            _tenantContext.TenantId.Value,
            storeId,
            _tenantContext.TerminalId,
            normalizedRequest,
            cancellationToken);

        if (adjustment is null)
        {
            _logger.LogWarning(
                "Inventory adjustment {LocalAdjustmentId} rejected by repository for tenant {TenantId} store {StoreId}",
                request.LocalAdjustmentId,
                _tenantContext.TenantId.Value,
                storeId);
            return null;
        }

        _logger.LogInformation(
            "Inventory adjustment {AdjustmentId} accepted for tenant {TenantId} store {StoreId} local adjustment {LocalAdjustmentId} lines {LineCount}",
            adjustment.Id,
            adjustment.TenantId,
            adjustment.StoreId,
            adjustment.LocalAdjustmentId,
            adjustment.Lines.Count);

        await WriteAuditAsync(adjustment.TenantId, "inventory.adjustment.created", "inventory_adjustment", adjustment.Id, adjustment, cancellationToken);

        return adjustment;
    }

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

    private bool TryResolveStore(Guid? requestStoreId, out Guid storeId)
    {
        storeId = Guid.Empty;

        if (_tenantContext.TerminalId.HasValue)
        {
            if (!_tenantContext.StoreId.HasValue)
            {
                return false;
            }

            if (requestStoreId.HasValue && requestStoreId.Value != _tenantContext.StoreId.Value)
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

    private static bool LinesAreValid(CreateInventoryAdjustmentRequest request, string adjustmentType)
    {
        foreach (CreateInventoryAdjustmentLineRequest line in request.Lines)
        {
            if (line.ProductId == Guid.Empty || line.UnitId == Guid.Empty || (line.CostCents.HasValue && line.CostCents.Value < 0))
            {
                return false;
            }

            if (!decimal.TryParse(line.QuantityDelta, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantityDelta) || quantityDelta == 0)
            {
                return false;
            }

            if (adjustmentType == "stock_in" && quantityDelta <= 0)
            {
                return false;
            }

            if ((adjustmentType == "stock_out" || adjustmentType == "waste") && quantityDelta >= 0)
            {
                return false;
            }
        }

        return true;
    }
}
