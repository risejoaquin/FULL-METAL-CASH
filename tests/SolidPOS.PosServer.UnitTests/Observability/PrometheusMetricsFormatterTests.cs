using SolidPOS.PosServer.Contracts.Observability;
using SolidPOS.PosServer.Infrastructure.Observability;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Observability;

public sealed class PrometheusMetricsFormatterTests
{
    [Fact]
    public void Prometheus_export_contains_required_v1_1_02_metrics_without_tenant_identifier()
    {
        OperationalMetricsResponse metrics = new(
            DateTimeOffset.UnixEpoch,
            new DatabaseMetricsResponse(true, "solidpos", "16", 2, 0, 7, true, []),
            new RequestMetricsResponse(100, 2, 20, 40, []),
            new SyncMetricsResponse(new Dictionary<string, long> { ["received"] = 1, ["processing"] = 0 }, 0, 0, 1, 0),
            new SalesLatencyMetricsResponse(1, 10, 10, 20),
            new PaymentMetricsResponse(0, 0),
            new InventoryRiskMetricsResponse(0, 0),
            new FinancialIntegrityMetricsResponse(0),
            new AuditTrailMetricsResponse(1, DateTimeOffset.UnixEpoch));

        string text = PrometheusMetricsFormatter.Format(metrics);

        Assert.Contains("solidpos_http_error_ratio 0.02", text);
        Assert.Contains("solidpos_postgresql_active_non_client_wait_event 0", text);
        Assert.Contains("solidpos_postgresql_client_read_wait_event 7", text);
        Assert.Contains("solidpos_sync_pending 1", text);
        Assert.Contains("solidpos_financial_sale_payment_mismatch 0", text);
        Assert.DoesNotContain("tenant_id", text, StringComparison.OrdinalIgnoreCase);
    }
}
