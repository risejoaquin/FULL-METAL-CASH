using SolidPOS.PosServer.Contracts.Observability;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Observability;

public sealed class OperationalMetricsContractTests
{
    [Fact]
    public void Metrics_contract_exposes_hardening_signals()
    {
        OperationalMetricsResponse response = new(
            DateTimeOffset.UnixEpoch,
            new DatabaseMetricsResponse(true, "solidpos", "16", 1, true, []),
            new RequestMetricsResponse(1, 0, 10, 10, []),
            new SyncMetricsResponse(new Dictionary<string, long> { ["processed"] = 1 }, 0, 1, 0, 0),
            new SalesLatencyMetricsResponse(1, 25, 10, 20),
            new PaymentMetricsResponse(0, 0),
            new InventoryRiskMetricsResponse(2, 1),
            new AuditTrailMetricsResponse(5, DateTimeOffset.UnixEpoch));

        Assert.True(response.Database.Ready);
        Assert.Equal(2, response.Inventory.NegativeInventoryItemCount);
        Assert.Equal(25, response.Sales.AveragePersistLatencyMsLast24Hours);
        Assert.Equal(5, response.Audit.AuditEventsLast24Hours);
    }
}
