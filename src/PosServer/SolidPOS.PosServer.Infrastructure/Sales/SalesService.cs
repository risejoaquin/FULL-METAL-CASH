using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Contracts.Receipts;
using SolidPOS.PosServer.Contracts.Sales;

namespace SolidPOS.PosServer.Infrastructure.Sales;

public sealed class SalesService : ISalesService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ISalesRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<SalesService> _logger;

    public SalesService(
        ITenantContext tenantContext,
        ISalesRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<SalesService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<SaleResponse?> CreateAsync(CreateSaleRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sale rejected because terminal runtime context is missing");
            return null;
        }

        if (request.LocalSaleId == Guid.Empty || request.CashierUserId == Guid.Empty)
        {
            _logger.LogWarning("Sale rejected because required identifiers are missing for tenant {TenantId}", _tenantContext.TenantId.Value);
            return null;
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            _logger.LogWarning("Sale {LocalSaleId} rejected because it has no lines for tenant {TenantId}", request.LocalSaleId, _tenantContext.TenantId.Value);
            return null;
        }

        if (request.Payments is null || request.Payments.Count == 0)
        {
            _logger.LogWarning("Sale {LocalSaleId} rejected because it has no payments for tenant {TenantId}", request.LocalSaleId, _tenantContext.TenantId.Value);
            return null;
        }

        if (request.TipCents < 0)
        {
            _logger.LogWarning("Sale {LocalSaleId} rejected because tip is negative for tenant {TenantId}", request.LocalSaleId, _tenantContext.TenantId.Value);
            return null;
        }

        if (!LinesAreValid(request) || !PaymentsAreValid(request))
        {
            _logger.LogWarning("Sale {LocalSaleId} rejected by request shape validation for tenant {TenantId}", request.LocalSaleId, _tenantContext.TenantId.Value);
            return null;
        }

        SaleResponse? sale = await _repository.CreateAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            request,
            cancellationToken);

        if (sale is null)
        {
            _logger.LogWarning(
                "Sale {LocalSaleId} rejected by repository for tenant {TenantId} store {StoreId} terminal {TerminalId}",
                request.LocalSaleId,
                _tenantContext.TenantId.Value,
                _tenantContext.StoreId.Value,
                _tenantContext.TerminalId.Value);
            return null;
        }

        _logger.LogInformation(
            "Sale {SaleId} accepted for tenant {TenantId} store {StoreId} terminal {TerminalId} local sale {LocalSaleId} total {TotalCents}",
            sale.Id,
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            sale.LocalSaleId,
            sale.TotalCents);

        await WriteAuditAsync(sale.TenantId, "sale.completed", "sale", sale.Id, sale, cancellationToken);

        return sale;
    }

    public async Task<SaleResponse?> VoidAsync(Guid saleId, VoidSaleRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sale void rejected because terminal runtime context is missing");
            return null;
        }

        if (saleId == Guid.Empty || request.VoidedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning("Sale void {SaleId} rejected because required fields are missing for tenant {TenantId}", saleId, _tenantContext.TenantId.Value);
            return null;
        }

        SaleResponse? sale = await _repository.VoidAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            saleId,
            request with { Reason = request.Reason.Trim() },
            cancellationToken);

        if (sale is null)
        {
            _logger.LogWarning(
                "Sale void {SaleId} rejected by repository for tenant {TenantId} store {StoreId} terminal {TerminalId} actor {ActorUserId}",
                saleId,
                _tenantContext.TenantId.Value,
                _tenantContext.StoreId.Value,
                _tenantContext.TerminalId.Value,
                request.VoidedByUserId);
            return null;
        }

        _logger.LogInformation(
            "Sale {SaleId} voided for tenant {TenantId} store {StoreId} terminal {TerminalId} actor {ActorUserId}",
            sale.Id,
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            request.VoidedByUserId);

        await WriteAuditAsync(sale.TenantId, "sale.voided", "sale", sale.Id, sale, cancellationToken);

        return sale;
    }

    public async Task<SaleResponse?> VoidByLocalSaleIdAsync(Guid localSaleId, VoidSaleRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.StoreId.HasValue || !_tenantContext.TerminalId.HasValue)
        {
            _logger.LogWarning("Sale void by local id rejected because terminal runtime context is missing");
            return null;
        }

        if (localSaleId == Guid.Empty || request.VoidedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning("Sale void by local sale {LocalSaleId} rejected because required fields are missing for tenant {TenantId}", localSaleId, _tenantContext.TenantId.Value);
            return null;
        }

        SaleResponse? sale = await _repository.VoidByLocalSaleIdAsync(
            _tenantContext.TenantId.Value,
            _tenantContext.StoreId.Value,
            _tenantContext.TerminalId.Value,
            localSaleId,
            request with { Reason = request.Reason.Trim() },
            cancellationToken);

        if (sale is null)
        {
            _logger.LogWarning(
                "Sale void by local sale {LocalSaleId} rejected by repository for tenant {TenantId} store {StoreId} terminal {TerminalId} actor {ActorUserId}",
                localSaleId,
                _tenantContext.TenantId.Value,
                _tenantContext.StoreId.Value,
                _tenantContext.TerminalId.Value,
                request.VoidedByUserId);
            return null;
        }

        _logger.LogInformation(
            "Sale {SaleId} voided by local sale {LocalSaleId} for tenant {TenantId} store {StoreId} terminal {TerminalId} actor {ActorUserId}",
            sale.Id,
            sale.LocalSaleId,
            sale.TenantId,
            sale.StoreId,
            sale.TerminalId,
            request.VoidedByUserId);

        await WriteAuditAsync(sale.TenantId, "sale.voided", "sale", sale.Id, sale, cancellationToken);

        return sale;
    }


    public async Task<SaleDetailResponse?> GetByIdAsync(Guid saleId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || saleId == Guid.Empty)
        {
            _logger.LogWarning("Sale detail read rejected because tenant context or sale id is missing");
            return null;
        }

        SaleDetailResponse? sale = await _repository.GetByIdAsync(_tenantContext.TenantId.Value, saleId, cancellationToken);
        if (sale is null)
        {
            _logger.LogWarning("Sale detail {SaleId} not found for tenant {TenantId}", saleId, _tenantContext.TenantId.Value);
        }

        return sale;
    }

    public async Task<IReadOnlyCollection<SaleListItemResponse>?> ListAsync(SaleListFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Sale list rejected because tenant context is missing");
            return null;
        }

        if (!ListFiltersAreValid(filters))
        {
            _logger.LogWarning("Sale list rejected by invalid filter shape for tenant {TenantId}", _tenantContext.TenantId.Value);
            return null;
        }

        return await _repository.ListAsync(_tenantContext.TenantId.Value, NormalizeFilters(filters), cancellationToken);
    }

    public async Task<ReceiptResponse?> GetReceiptAsync(Guid saleId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || saleId == Guid.Empty)
        {
            _logger.LogWarning("Receipt read rejected because tenant context or sale id is missing");
            return null;
        }

        ReceiptResponse? receipt = await _repository.GetReceiptAsync(_tenantContext.TenantId.Value, saleId, cancellationToken);
        if (receipt is null)
        {
            _logger.LogWarning("Receipt for sale {SaleId} not found for tenant {TenantId}", saleId, _tenantContext.TenantId.Value);
        }

        return receipt;
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


    private static bool ListFiltersAreValid(SaleListFilters filters)
    {
        if (filters.From.HasValue && filters.To.HasValue && filters.From > filters.To)
        {
            return false;
        }

        if (filters.Limit < 1 || filters.Limit > 200)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filters.Status))
        {
            string status = filters.Status.Trim();
            return status is "suspended" or "completed" or "voided" or "partially_returned" or "returned";
        }

        return true;
    }

    private static SaleListFilters NormalizeFilters(SaleListFilters filters)
    {
        return filters with
        {
            Status = string.IsNullOrWhiteSpace(filters.Status) ? null : filters.Status.Trim(),
            Limit = Math.Clamp(filters.Limit, 1, 200)
        };
    }

    private static bool LinesAreValid(CreateSaleRequest request)
    {
        foreach (CreateSaleLineRequest line in request.Lines)
        {
            if (line.ProductId == Guid.Empty || line.DiscountCents < 0)
            {
                return false;
            }

            if (!decimal.TryParse(line.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity) || quantity <= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PaymentsAreValid(CreateSaleRequest request)
    {
        HashSet<Guid> localPaymentIds = [];
        foreach (CreateSalePaymentRequest payment in request.Payments)
        {
            if (payment.LocalPaymentId == Guid.Empty ||
                payment.AmountCents <= 0 ||
                string.IsNullOrWhiteSpace(payment.MethodCode) ||
                !localPaymentIds.Add(payment.LocalPaymentId))
            {
                return false;
            }
        }

        return true;
    }
}
