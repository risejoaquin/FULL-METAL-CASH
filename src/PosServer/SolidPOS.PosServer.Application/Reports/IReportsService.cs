using SolidPOS.PosServer.Contracts.Reports;

namespace SolidPOS.PosServer.Application.Reports;

public interface IReportsService
{
    Task<SalesRangeReportResponse?> GetSalesRangeAsync(ReportDateRangeFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PaymentMethodReportItemResponse>?> GetSalesByPaymentMethodAsync(ReportDateRangeFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CashShiftReportItemResponse>?> GetCashShiftsAsync(ReportListFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TopProductReportItemResponse>?> GetTopProductsAsync(ReportListFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NegativeInventoryItemResponse>?> GetNegativeInventoryAsync(Guid? storeId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<InventoryMovementReportItemResponse>?> GetInventoryMovementsAsync(ReportListFilters filters, CancellationToken cancellationToken);

    Task<DashboardOverviewResponse?> GetDashboardOverviewAsync(DashboardReportFilters filters, CancellationToken cancellationToken);
}
