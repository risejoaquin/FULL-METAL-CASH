namespace SolidPOS.PosServer.Application.Reports;

public sealed record ReportDateRangeFilters(
    Guid? StoreId,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record ReportListFilters(
    Guid? StoreId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit);

public sealed record DashboardReportFilters(
    Guid? StoreId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit,
    string? TrendBucket);
