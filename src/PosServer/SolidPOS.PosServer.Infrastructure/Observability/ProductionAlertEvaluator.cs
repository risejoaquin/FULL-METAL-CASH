using Microsoft.Extensions.Configuration;
using SolidPOS.PosServer.Contracts.Observability;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public static class ProductionAlertEvaluator
{
    public static ProductionAlertsResponse Evaluate(OperationalMetricsResponse metrics, IConfiguration configuration)
    {
        double errorRatio = metrics.Requests.TotalRequests == 0
            ? 0
            : (double)metrics.Requests.FailedRequests / metrics.Requests.TotalRequests;
        long pending = Status(metrics.Sync.InboxByStatus, "received");
        long processing = Status(metrics.Sync.InboxByStatus, "processing");
        long retryPending = metrics.Sync.RetryPendingEvents;

        int minimumRequests = configuration.GetValue("Observability:Alerts:MinimumRequestsForErrorRateAlert", 20);
        double errorRatioThreshold = configuration.GetValue("Observability:Alerts:HttpErrorRatio", 0.05d);
        double p95Threshold = configuration.GetValue("Observability:Alerts:HttpP95LatencyMs", 1200d);
        int serverWaitThreshold = configuration.GetValue("Observability:Alerts:ActiveNonClientWaitEventCount", 0);
        int idleInTransactionThreshold = configuration.GetValue("Observability:Alerts:IdleInTransactionCount", 0);
        int longRunningQueryThreshold = configuration.GetValue("Observability:Alerts:LongRunningQueryCount", 0);
        int syncQueueThreshold = configuration.GetValue("Observability:Alerts:SyncNonConvergedCount", 0);
        int deadLetterThreshold = configuration.GetValue("Observability:Alerts:DeadLetterCount", 1);
        int negativeStockThreshold = configuration.GetValue("Observability:Alerts:NegativeStockCount", 0);
        int salePaymentMismatchThreshold = configuration.GetValue("Observability:Alerts:SalePaymentMismatchCount", 0);

        List<ProductionAlertResponse> alerts =
        [
            Alert(
                "http_error_ratio",
                "warning",
                metrics.Requests.TotalRequests >= minimumRequests && errorRatio > errorRatioThreshold,
                errorRatio,
                errorRatioThreshold,
                ">",
                $"HTTP error ratio. Evaluation starts after {minimumRequests} observed requests."),
            Alert("http_p95_latency_ms", "warning", metrics.Requests.P95LatencyMs > p95Threshold, metrics.Requests.P95LatencyMs, p95Threshold, ">", "HTTP p95 latency exceeded the approved alert threshold."),
            Alert("postgres_active_non_client_wait_event", "warning", metrics.Database.ActiveNonClientWaitEventCount > serverWaitThreshold, metrics.Database.ActiveNonClientWaitEventCount, serverWaitThreshold, ">", "Only active non-client PostgreSQL waits are pressure signals; ClientRead is diagnostic only."),
            Alert("postgres_idle_in_transaction", "critical", metrics.Database.IdleInTransactionCount > idleInTransactionThreshold, metrics.Database.IdleInTransactionCount, idleInTransactionThreshold, ">", "Unexpected PostgreSQL idle-in-transaction sessions must remain at zero."),
            Alert("postgres_long_running_query", "warning", metrics.Database.LongRunningQueryCount > longRunningQueryThreshold, metrics.Database.LongRunningQueryCount, longRunningQueryThreshold, ">", "Active PostgreSQL queries exceeded the 500ms slow-query visibility threshold."),
            Alert("sync_queue_not_converged", "warning", pending + processing + retryPending > syncQueueThreshold, pending + processing + retryPending, syncQueueThreshold, ">", "received + processing + retry_pending must converge to zero."),
            Alert("sync_dead_letter", "critical", metrics.Sync.DeadLetterEvents > deadLetterThreshold, metrics.Sync.DeadLetterEvents, deadLetterThreshold, ">", "Dead-letter events exceeded the accepted Public GA baseline."),
            Alert("negative_stock", "critical", metrics.Inventory.NegativeInventoryItemCount > negativeStockThreshold, metrics.Inventory.NegativeInventoryItemCount, negativeStockThreshold, ">", "Negative inventory positions are not accepted."),
            Alert("sale_payment_mismatch", "critical", metrics.FinancialIntegrity.SalePaymentMismatchCount > salePaymentMismatchThreshold, metrics.FinancialIntegrity.SalePaymentMismatchCount, salePaymentMismatchThreshold, ">", "Completed sale paid_cents does not reconcile with approved payments.")
        ];

        return new ProductionAlertsResponse(metrics.GeneratedAt, alerts.Any(static alert => alert.Active), alerts);
    }

    private static long Status(IReadOnlyDictionary<string, long> statuses, string status)
        => statuses.TryGetValue(status, out long value) ? value : 0;

    private static ProductionAlertResponse Alert(string code, string severity, bool active, double observed, double threshold, string comparator, string detail)
        => new(code, severity, active, observed, threshold, comparator, detail);
}
