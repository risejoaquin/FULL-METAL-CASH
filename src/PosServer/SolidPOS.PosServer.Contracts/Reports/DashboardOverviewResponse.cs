namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record DashboardOverviewResponse(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset From,
    DateTimeOffset To,
    string TrendBucket,
    SalesRangeReportResponse Sales,
    IReadOnlyCollection<PaymentMethodReportItemResponse> PaymentMethods,
    IReadOnlyCollection<TopProductReportItemResponse> TopProducts,
    DashboardInventorySummaryResponse Inventory,
    IReadOnlyCollection<NegativeInventoryItemResponse> NegativeInventory,
    IReadOnlyCollection<CashShiftReportItemResponse> RecentCashShifts,
    IReadOnlyCollection<DashboardSalesTrendPointResponse> SalesTrend);
