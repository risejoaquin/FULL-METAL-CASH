using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Returns;
using SolidPOS.PosServer.Contracts.Returns;

namespace SolidPOS.PosServer.Infrastructure.Returns;

public sealed class ReturnsService : IReturnsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly IReturnsRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<ReturnsService> _logger;

    public ReturnsService(
        ITenantContext tenantContext,
        IReturnsRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<ReturnsService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<ReturnResponse?> CreateAsync(CreateReturnRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Return rejected because terminal runtime context is missing");
            return null;
        }

        if (!RequestIsValid(request))
        {
            _logger.LogWarning("Return rejected by request shape validation for tenant {TenantId} sale {SaleId}", _tenantContext.TenantId.Value, request.SaleId);
            return null;
        }

        ReturnResponse? created = await _repository.CreateAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            request with { Reason = request.Reason.Trim() },
            cancellationToken);

        if (created is null)
        {
            _logger.LogWarning("Return rejected by repository for tenant {TenantId} sale {SaleId}", _tenantContext.TenantId.Value, request.SaleId);
            return null;
        }

        await _auditEventWriter.AppendAsync(
            created.TenantId,
            "return.created",
            "return",
            created.Id,
            null,
            JsonSerializer.SerializeToElement(created, JsonOptions),
            cancellationToken);

        return created;
    }

    public async Task<ReturnResponse?> GetByIdAsync(Guid returnId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || returnId == Guid.Empty)
        {
            return null;
        }

        return await _repository.GetByIdAsync(_tenantContext.TenantId.Value, returnId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ReturnListItemResponse>?> ListAsync(ReturnListFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !FiltersAreValid(filters))
        {
            return null;
        }

        ReturnListFilters normalized = filters with { Limit = filters.Limit <= 0 ? 50 : filters.Limit };
        return await _repository.ListAsync(_tenantContext.TenantId.Value, normalized, cancellationToken);
    }

    private static bool RequestIsValid(CreateReturnRequest request)
    {
        if (request.LocalReturnId == Guid.Empty || request.SaleId == Guid.Empty || request.CreatedByUserId == Guid.Empty)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return false;
        }

        if (request.Lines is null || request.Lines.Count == 0 || request.Refunds is null || request.Refunds.Count == 0)
        {
            return false;
        }

        foreach (CreateReturnLineRequest line in request.Lines)
        {
            if (line.SaleLineId == Guid.Empty || !decimal.TryParse(line.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity) || quantity <= 0)
            {
                return false;
            }
        }

        return request.Refunds.All(refund => !string.IsNullOrWhiteSpace(refund.MethodCode) && refund.AmountCents > 0);
    }

    private static bool FiltersAreValid(ReturnListFilters filters)
    {
        if (filters.From.HasValue && filters.To.HasValue && filters.From > filters.To)
        {
            return false;
        }

        return filters.Limit is >= 0 and <= 200;
    }
}
