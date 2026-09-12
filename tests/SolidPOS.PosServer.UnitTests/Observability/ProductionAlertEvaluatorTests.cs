using Microsoft.Extensions.Configuration;
using SolidPOS.PosServer.Contracts.Observability;
using SolidPOS.PosServer.Infrastructure.Observability;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Observability;

public sealed class ProductionAlertEvaluatorTests
{
    [Fact]
    public void ClientRead_is_not_used_as_database_pressure_blocker()
    {
        OperationalMetricsResponse metrics = CreateMetrics(
            activeNonClientWaitEventCount: 0,
            clientReadWaitEventCount: 12);

        ProductionAlertsResponse alerts = ProductionAlertEvaluator.Evaluate(metrics, Configuration());

        ProductionAlertResponse databaseAlert = Assert.Single(alerts.Alerts, x => x.Code == "postgres_active_non_client_wait_event");
        Assert.False(databaseAlert.Active);
    }

    [Fact]
    public void Active_non_client_wait_event_is_alertable()
    {
        OperationalMetricsResponse metrics = CreateMetrics(
            activeNonClientWaitEventCount: 1,
            clientReadWaitEventCount: 0);

        ProductionAlertsResponse alerts = ProductionAlertEvaluator.Evaluate(metrics, Configuration());

        ProductionAlertResponse databaseAlert = Assert.Single(alerts.Alerts, x => x.Code == "postgres_active_non_client_wait_event");
        Assert.True(databaseAlert.Active);
    }

    [Fact]
    public void Public_ga_integrity_baselines_are_not_relaxed()
    {
        OperationalMetricsResponse metrics = CreateMetrics(
            deadLetters: 2,
            negativeStock: 1,
            salePaymentMismatch: 1);

        ProductionAlertsResponse alerts = ProductionAlertEvaluator.Evaluate(metrics, Configuration());

        Assert.True(Assert.Single(alerts.Alerts, x => x.Code == "sync_dead_letter").Active);
        Assert.True(Assert.Single(alerts.Alerts, x => x.Code == "negative_stock").Active);
        Assert.True(Assert.Single(alerts.Alerts, x => x.Code == "sale_payment_mismatch").Active);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>())
        .Build();

    private static OperationalMetricsResponse CreateMetrics(
        int activeNonClientWaitEventCount = 0,
        int clientReadWaitEventCount = 0,
        long deadLetters = 0,
        long negativeStock = 0,
        long salePaymentMismatch = 0)
        => new(
            DateTimeOffset.UnixEpoch,
            new DatabaseMetricsResponse(true, "solidpos", "16", 1, activeNonClientWaitEventCount, clientReadWaitEventCount, 0, 0, 0, 0, true, []),
            new RequestMetricsResponse(100, 0, 10, 20, []),
            new SyncMetricsResponse(new Dictionary<string, long> { ["received"] = 0, ["processing"] = 0 }, 0, 0, deadLetters, 0),
            new SalesLatencyMetricsResponse(1, 10, 10, 20),
            new PaymentMetricsResponse(0, 0),
            new InventoryRiskMetricsResponse(negativeStock, 0),
            new FinancialIntegrityMetricsResponse(salePaymentMismatch),
            new AuditTrailMetricsResponse(1, DateTimeOffset.UnixEpoch));
}
