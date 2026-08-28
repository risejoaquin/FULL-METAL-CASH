using SolidPOS.PosServer.Infrastructure.Observability;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Observability;

public sealed class OperationalMetricsRecorderTests
{
    [Fact]
    public void Snapshot_aggregates_request_counts_failures_and_latency()
    {
        OperationalMetricsRecorder recorder = new();

        recorder.Record("GET", "/api/v1/sales", 200, 10);
        recorder.Record("GET", "/api/v1/sales", 500, 30);
        recorder.Record("POST", "/api/v1/sync/push", 200, 20);

        RequestMetricsSnapshot snapshot = recorder.Snapshot();

        Assert.Equal(3, snapshot.TotalRequests);
        Assert.Equal(1, snapshot.FailedRequests);
        Assert.True(snapshot.AverageLatencyMs > 0);
        Assert.Contains(snapshot.Routes, route => route.Route == "/api/v1/sales" && route.Count == 2 && route.FailedCount == 1);
    }
}
