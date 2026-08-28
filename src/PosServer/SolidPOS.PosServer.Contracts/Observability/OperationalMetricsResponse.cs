namespace SolidPOS.PosServer.Contracts.Observability;

public sealed record OperationalMetricsResponse(
    DateTimeOffset GeneratedAt,
    DatabaseMetricsResponse Database,
    RequestMetricsResponse Requests,
    SyncMetricsResponse Sync,
    SalesLatencyMetricsResponse Sales,
    PaymentMetricsResponse Payments,
    InventoryRiskMetricsResponse Inventory,
    AuditTrailMetricsResponse Audit);

public sealed record DatabaseMetricsResponse(
    bool Ready,
    string DatabaseName,
    string ServerVersion,
    int ActiveConnections,
    bool RequiredTablesPresent,
    IReadOnlyList<string> MissingRequiredTables);

public sealed record RequestMetricsResponse(
    long TotalRequests,
    long FailedRequests,
    double AverageLatencyMs,
    double P95LatencyMs,
    IReadOnlyList<RequestRouteMetricResponse> TopRoutes);

public sealed record RequestRouteMetricResponse(
    string Method,
    string Route,
    long Count,
    long FailedCount,
    double AverageLatencyMs,
    double P95LatencyMs);

public sealed record SyncMetricsResponse(
    IReadOnlyDictionary<string, long> InboxByStatus,
    long PendingConflicts,
    long ResolvedConflicts,
    long DeadLetterEvents,
    long RetryPendingEvents);

public sealed record SalesLatencyMetricsResponse(
    long SalesLast24Hours,
    double AveragePersistLatencyMsLast24Hours,
    double ApiAverageLatencyMs,
    double ApiP95LatencyMs);

public sealed record PaymentMetricsResponse(
    long FailedPaymentsLast24Hours,
    long DeclinedPaymentsLast24Hours);

public sealed record InventoryRiskMetricsResponse(
    long NegativeInventoryItemCount,
    long LowStockItemCount);

public sealed record AuditTrailMetricsResponse(
    long AuditEventsLast24Hours,
    DateTimeOffset? LastAuditEventAt);
