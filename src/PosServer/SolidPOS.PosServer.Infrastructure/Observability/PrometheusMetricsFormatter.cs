using System.Globalization;
using System.Text;
using SolidPOS.PosServer.Contracts.Observability;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public static class PrometheusMetricsFormatter
{
    public static string Format(OperationalMetricsResponse metrics)
    {
        StringBuilder output = new();
        Append(output, "solidpos_http_requests_total", metrics.Requests.TotalRequests, "Process-lifetime HTTP requests observed by PosServer.", "counter");
        Append(output, "solidpos_http_failed_requests_total", metrics.Requests.FailedRequests, "Process-lifetime HTTP requests with status >= 400.", "counter");

        double errorRatio = metrics.Requests.TotalRequests == 0
            ? 0
            : (double)metrics.Requests.FailedRequests / metrics.Requests.TotalRequests;
        Append(output, "solidpos_http_error_ratio", errorRatio, "HTTP error ratio over the in-process request recorder snapshot.", "gauge");
        Append(output, "solidpos_http_p95_latency_ms", metrics.Requests.P95LatencyMs, "HTTP p95 latency in milliseconds.", "gauge");

        Append(output, "solidpos_postgresql_active_connections", metrics.Database.ActiveConnections, "PostgreSQL connections for the current database.", "gauge");
        Append(output, "solidpos_postgresql_active_non_client_wait_event", metrics.Database.ActiveNonClientWaitEventCount, "Active PostgreSQL sessions waiting on non-client server events.", "gauge");
        Append(output, "solidpos_postgresql_client_read_wait_event", metrics.Database.ClientReadWaitEventCount, "PostgreSQL ClientRead waits exposed for diagnostics only; never a blocker by itself.", "gauge");

        long pending = Status(metrics.Sync.InboxByStatus, "received");
        long processing = Status(metrics.Sync.InboxByStatus, "processing");
        Append(output, "solidpos_sync_pending", pending, "Tenant-scoped sync events waiting in received state.", "gauge");
        Append(output, "solidpos_sync_processing", processing, "Tenant-scoped sync events currently processing.", "gauge");
        Append(output, "solidpos_sync_retry_pending", metrics.Sync.RetryPendingEvents, "Tenant-scoped sync events waiting for retry.", "gauge");
        Append(output, "solidpos_sync_dead_letter", metrics.Sync.DeadLetterEvents, "Tenant-scoped sync dead-letter events.", "gauge");

        Append(output, "solidpos_inventory_negative_stock", metrics.Inventory.NegativeInventoryItemCount, "Tenant-scoped inventory positions below zero.", "gauge");
        Append(output, "solidpos_financial_sale_payment_mismatch", metrics.FinancialIntegrity.SalePaymentMismatchCount, "Tenant-scoped completed sales whose approved payment total does not equal paid_cents.", "gauge");
        Append(output, "solidpos_metrics_generated_at_seconds", metrics.GeneratedAt.ToUnixTimeSeconds(), "Unix timestamp of this metrics snapshot.", "gauge");
        return output.ToString();
    }

    private static long Status(IReadOnlyDictionary<string, long> statuses, string status)
        => statuses.TryGetValue(status, out long value) ? value : 0;

    private static void Append(StringBuilder output, string name, long value, string help, string type)
        => Append(output, name, value.ToString(CultureInfo.InvariantCulture), help, type);

    private static void Append(StringBuilder output, string name, double value, string help, string type)
        => Append(output, name, value.ToString("0.########", CultureInfo.InvariantCulture), help, type);

    private static void Append(StringBuilder output, string name, string value, string help, string type)
    {
        output.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        output.Append("# TYPE ").Append(name).Append(' ').AppendLine(type);
        output.Append(name).Append(' ').AppendLine(value);
    }
}
