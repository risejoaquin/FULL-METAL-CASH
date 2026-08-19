using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class LocalOutboxBatchPlannerTests
{
    [Fact]
    public void CreateBatch_orders_events_by_sequence_number()
    {
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var first = CreateEvent(tenantId, storeId, terminalId, 20);
        var second = CreateEvent(tenantId, storeId, terminalId, 10);

        var batch = LocalOutboxBatchPlanner.CreateBatch(Guid.NewGuid(), new[] { first, second });

        Assert.Equal(10, batch.Events[0].SequenceNumber);
        Assert.Equal(20, batch.Events[1].SequenceNumber);
    }

    [Fact]
    public void CreateBatch_rejects_mixed_terminal_scope()
    {
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var first = CreateEvent(tenantId, storeId, Guid.NewGuid(), 10);
        var second = CreateEvent(tenantId, storeId, Guid.NewGuid(), 20);

        var error = Assert.Throws<InvalidOperationException>(() => LocalOutboxBatchPlanner.CreateBatch(Guid.NewGuid(), new[] { first, second }));

        Assert.Contains("cannot mix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LocalOutboxEvent CreateEvent(Guid tenantId, Guid storeId, Guid terminalId, long sequenceNumber) => new(
        Guid.NewGuid(),
        tenantId,
        storeId,
        terminalId,
        "sale.completed",
        4,
        sequenceNumber,
        "{}",
        LocalOutboxStatus.Pending,
        DateTimeOffset.UtcNow);
}
