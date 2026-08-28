using System.Text.Json;
using SolidPOS.PosServer.Contracts.Sync;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sync;

public sealed class SyncPushContractTests
{
    [Fact]
    public void Sync_push_contract_can_represent_offline_outbox_batch()
    {
        Guid batchId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();

        SyncPushRequest request = new(
            batchId,
            [
                new SyncPushEventRequest(
                    eventId,
                    "sale.completed",
                    "sale",
                    saleId,
                    DateTimeOffset.UtcNow,
                    1,
                    JsonDocument.Parse("""{"local_sale_id":"abc"}""").RootElement.Clone())
            ]);

        SyncPushResponse response = new(
            batchId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            1,
            0,
            0,
            [new SyncPushEventResultResponse(eventId, "accepted", null)]);

        Assert.Equal(batchId, request.BatchId);
        Assert.Single(request.Events);
        Assert.Equal("accepted", response.Results.Single().Status);
    }

    [Fact]
    public void Sync_process_contract_can_represent_processor_result()
    {
        Guid eventId = Guid.NewGuid();

        SyncProcessResponse response = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            1,
            0,
            [new SyncProcessEventResultResponse(Guid.NewGuid(), eventId, "sale.completed", "processed", null, null)]);

        Assert.Equal(1, response.ProcessedCount);
        Assert.Equal("processed", response.Results.Single().Status);
    }
}
