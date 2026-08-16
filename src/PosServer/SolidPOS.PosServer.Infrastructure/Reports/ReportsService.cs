using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Reports;
using SolidPOS.PosServer.Contracts.Reports;

namespace SolidPOS.PosServer.Infrastructure.Reports;

public sealed class ReportsService : IReportsService
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 200;

    private readonly ITenantContext _tenantContext;
    private readonly IReportsRepository _repository;
    private readonly ILogger<ReportsService> _logger;

    public ReportsService(
        ITenantContext tenantContext,
        IReportsRepository repository,
        ILogger<ReportsService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _logger = logger;
    }

    public async Task<SalesRangeReportResponse?> GetSalesRangeAsync(ReportDateRangeFilters filters, CancellationToken cancellationToken)
    {
        ReportDateRangeFilters? normalized = await NormalizeDateRangeAsync(filters, cancellationToken);
        if (normalized is null || !_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        SalesRangeReportResponse response = await _repository.GetSalesRangeAsync(tenantId, normalized, cancellationToken);
        _logger.LogInformation("Sales range report read for tenant {TenantId} store {StoreId} from {From} to {To}", tenantId, normalized.StoreId, response.From, response.To);
        return response;
    }

    public async Task<IReadOnlyCollection<PaymentMethodReportItemResponse>?> GetSalesByPaymentMethodAsync(ReportDateRangeFilters filters, CancellationToken cancellationToken)
    {
        ReportDateRangeFilters? normalized = await NormalizeDateRangeAsync(filters, cancellationToken);
        if (normalized is null || !_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        IReadOnlyCollection<PaymentMethodReportItemResponse> response = await _repository.GetSalesByPaymentMethodAsync(tenantId, normalized, cancellationToken);
        _logger.LogInformation("Payment method report read for tenant {TenantId} store {StoreId} items {ItemCount}", tenantId, normalized.StoreId, response.Count);
        return response;
    }

    public async Task<IReadOnlyCollection<CashShiftReportItemResponse>?> GetCashShiftsAsync(ReportListFilters filters, CancellationToken cancellationToken)
    {
        ReportListFilters? normalized = await NormalizeListFiltersAsync(filters, cancellationToken);
        if (normalized is null || !_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        IReadOnlyCollection<CashShiftReportItemResponse> response = await _repository.GetCashShiftsAsync(tenantId, normalized, cancellationToken);
        _logger.LogInformation("Cash shifts report read for tenant {TenantId} store {StoreId} items {ItemCount}", tenantId, normalized.StoreId, response.Count);
        return response;
    }

    public async Task<IReadOnlyCollection<TopProductReportItemResponse>?> GetTopProductsAsync(ReportListFilters filters, CancellationToken cancellationToken)
    {
        ReportListFilters? normalized = await NormalizeListFiltersAsync(filters, cancellationToken);
        if (normalized is null || !_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        IReadOnlyCollection<TopProductReportItemResponse> response = await _repository.GetTopProductsAsync(tenantId, normalized, cancellationToken);
        _logger.LogInformation("Top products report read for tenant {TenantId} store {StoreId} items {ItemCount}", tenantId, normalized.StoreId, response.Count);
        return response;
    }

    public async Task<IReadOnlyCollection<NegativeInventoryItemResponse>?> GetNegativeInventoryAsync(Guid? storeId, int limit, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId) || !await StoreFilterIsValidAsync(tenantId, storeId, cancellationToken))
        {
            return null;
        }

        int effectiveLimit = NormalizeLimit(limit);
        IReadOnlyCollection<NegativeInventoryItemResponse> response = await _repository.GetNegativeInventoryAsync(tenantId, storeId, effectiveLimit, cancellationToken);
        _logger.LogInformation("Negative inventory report read for tenant {TenantId} store {StoreId} items {ItemCount}", tenantId, storeId, response.Count);
        return response;
    }

    public async Task<IReadOnlyCollection<InventoryMovementReportItemResponse>?> GetInventoryMovementsAsync(ReportListFilters filters, CancellationToken cancellationToken)
    {
        ReportListFilters? normalized = await NormalizeListFiltersAsync(filters, cancellationToken);
        if (normalized is null || !_tenantContext.TenantId.HasValue)
        {
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        IReadOnlyCollection<InventoryMovementReportItemResponse> response = await _repository.GetInventoryMovementsAsync(tenantId, normalized, cancellationToken);
        _logger.LogInformation("Inventory movements report read for tenant {TenantId} store {StoreId} items {ItemCount}", tenantId, normalized.StoreId, response.Count);
        return response;
    }

    public async Task<DashboardOverviewResponse?> GetDashboardOverviewAsync(DashboardReportFilters filters, CancellationToken cancellationToken)
    {
        ReportDateRangeFilters? range = await NormalizeDateRangeAsync(
            new ReportDateRangeFilters(filters.StoreId, filters.From, filters.To),
            cancellationToken);
        if (range is null || !TryGetTenantId(out Guid tenantId))
        {
            return null;
        }

        int limit = NormalizeLimit(filters.Limit);
        string trendBucket = NormalizeTrendBucket(filters.TrendBucket, range.From!.Value, range.To!.Value);
        ReportListFilters listFilters = new(range.StoreId, range.From, range.To, limit);

        SalesRangeReportResponse sales = await _repository.GetSalesRangeAsync(tenantId, range, cancellationToken);
        IReadOnlyCollection<PaymentMethodReportItemResponse> paymentMethods = await _repository.GetSalesByPaymentMethodAsync(tenantId, range, cancellationToken);
        IReadOnlyCollection<TopProductReportItemResponse> topProducts = await _repository.GetTopProductsAsync(tenantId, listFilters, cancellationToken);
        IReadOnlyCollection<NegativeInventoryItemResponse> negativeInventory = await _repository.GetNegativeInventoryAsync(tenantId, range.StoreId, limit, cancellationToken);
        IReadOnlyCollection<CashShiftReportItemResponse> cashShifts = await _repository.GetCashShiftsAsync(tenantId, listFilters, cancellationToken);
        DashboardInventorySummaryResponse inventory = await _repository.GetDashboardInventorySummaryAsync(tenantId, range, cancellationToken);
        IReadOnlyCollection<DashboardSalesTrendPointResponse> salesTrend = await _repository.GetSalesTrendAsync(tenantId, range, trendBucket, cancellationToken);

        _logger.LogInformation(
            "Dashboard overview read for tenant {TenantId} store {StoreId} from {From} to {To} bucket {TrendBucket}",
            tenantId, range.StoreId, range.From, range.To, trendBucket);

        return new DashboardOverviewResponse(
            tenantId,
            range.StoreId,
            range.From.Value,
            range.To.Value,
            trendBucket,
            sales,
            paymentMethods,
            topProducts,
            inventory,
            negativeInventory,
            cashShifts,
            salesTrend);
    }

    private async Task<ReportDateRangeFilters?> NormalizeDateRangeAsync(ReportDateRangeFilters filters, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId))
        {
            return null;
        }

        DateTimeOffset to = filters.To ?? DateTimeOffset.UtcNow;
        DateTimeOffset from = filters.From ?? to.AddDays(-1);
        if (to < from)
        {
            _logger.LogWarning("Report rejected for tenant {TenantId}: invalid date range", tenantId);
            return null;
        }

        if (!await StoreFilterIsValidAsync(tenantId, filters.StoreId, cancellationToken))
        {
            return null;
        }

        return filters with { From = from, To = to };
    }

    private async Task<ReportListFilters?> NormalizeListFiltersAsync(ReportListFilters filters, CancellationToken cancellationToken)
    {
        ReportDateRangeFilters? dateFilters = await NormalizeDateRangeAsync(
            new ReportDateRangeFilters(filters.StoreId, filters.From, filters.To),
            cancellationToken);
        if (dateFilters is null)
        {
            return null;
        }

        return filters with
        {
            From = dateFilters.From,
            To = dateFilters.To,
            Limit = NormalizeLimit(filters.Limit)
        };
    }

    private async Task<bool> StoreFilterIsValidAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken)
    {
        if (!storeId.HasValue)
        {
            return true;
        }

        bool exists = await _repository.StoreExistsAsync(tenantId, storeId.Value, cancellationToken);
        if (!exists)
        {
            _logger.LogWarning("Report rejected for tenant {TenantId}: store filter {StoreId} is not owned by tenant", tenantId, storeId.Value);
        }

        return exists;
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = _tenantContext.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning("Report rejected because tenant context is missing");
            return false;
        }

        return true;
    }

    private static int NormalizeLimit(int limit)
    {
        return limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
    }

    private static string NormalizeTrendBucket(string? requested, DateTimeOffset from, DateTimeOffset to)
    {
        string? bucket = requested?.Trim().ToLowerInvariant();
        if (bucket is "hour" or "day")
        {
            return bucket;
        }

        return (to - from).TotalHours <= 72 ? "hour" : "day";
    }
}
