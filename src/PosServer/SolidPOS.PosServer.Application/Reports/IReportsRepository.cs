using SolidPOS.PosServer.Contracts.Reports;

namespace SolidPOS.PosServer.Application.Reports;

public interface IReportsRepository
{
    Task<bool> StoreExistsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);

    Task<SalesRangeReportResponse> GetSalesRangeAsync(Guid tenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PaymentMethodReportItemResponse>> GetSalesByPaymentMethodAsync(Guid tenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CashShiftReportItemResponse>> GetCashShiftsAsync(Guid tenantId, ReportListFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TopProductReportItemResponse>> GetTopProductsAsync(Guid tenantId, ReportListFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NegativeInventoryItemResponse>> GetNegativeInventoryAsync(Guid tenantId, Guid? storeId, int limit, CancellationToken cancellationToken);

    Task<long> GetNegativeInventoryCountAsync(Guid tenantId, Guid? storeId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryMovementReportItemResponse>> GetInventoryMovementsAsync(Guid tenantId, ReportListFilters filters, CancellationToken cancellationToken);

    Task<DashboardInventorySummaryResponse> GetDashboardInventorySummaryAsync(Guid tenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<DashboardSalesTrendPointResponse>> GetSalesTrendAsync(Guid tenantId, ReportDateRangeFilters filters, string trendBucket, CancellationToken cancellationToken);
}
