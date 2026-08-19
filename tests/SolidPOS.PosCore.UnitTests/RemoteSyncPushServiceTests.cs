using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class RemoteSyncPushServiceTests
{
    [Fact]
    public async Task PushPendingAsync_marks_events_synced_when_remote_acknowledges_batch()
    {
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var outboxEvent = new LocalOutboxEvent(
            eventId,
            tenantId,
            storeId,
            terminalId,
            "sale.completed",
            4,
            100,
            $"{{\"saleId\":\"{saleId}\"}}",
            LocalOutboxStatus.Pending,
            DateTimeOffset.UtcNow);
        var repository = new InMemoryLocalPosRepository(new[] { outboxEvent });
        var client = new SuccessfulRemoteSyncClient();
        var service = new RemoteSyncPushService(repository, client, new FixedClock());

        var result = await service.PushPendingAsync(500, Guid.NewGuid(), "terminal-token");

        Assert.NotNull(result);
        Assert.Equal(1, result!.AcceptedCount);
        Assert.Contains(eventId, repository.SyncedEvents);
        Assert.Single(repository.Acknowledgements);
        Assert.Equal("sale", client.LastRequest!.Events[0].EntityType);
        Assert.Equal(saleId, client.LastRequest.Events[0].EntityId);
    }

    [Fact]
    public async Task PushPendingAsync_returns_null_when_no_pending_events_exist()
    {
        var repository = new InMemoryLocalPosRepository(Array.Empty<LocalOutboxEvent>());
        var service = new RemoteSyncPushService(repository, new SuccessfulRemoteSyncClient(), new FixedClock());

        var result = await service.PushPendingAsync(500, Guid.NewGuid(), "terminal-token");

        Assert.Null(result);
    }

    private sealed class SuccessfulRemoteSyncClient : IRemoteSyncClient
    {
        public RemoteSyncPushRequest? LastRequest { get; private set; }

        public Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new RemoteSyncPushResult(
                request.BatchId,
                request.Events.Count,
                request.Events.Count,
                0,
                0,
                "{\"acceptedCount\":1,\"duplicateCount\":0,\"failedCount\":0}",
                request.Events.Select(item => item.EventId).ToArray()));
        }
    }

    private sealed class InMemoryLocalPosRepository : ILocalPosRepository
    {
        private readonly IReadOnlyList<LocalOutboxEvent> _pending;

        public InMemoryLocalPosRepository(IReadOnlyList<LocalOutboxEvent> pending) => _pending = pending;

        public List<Guid> SyncedEvents { get; } = new();
        public List<LocalSyncAcknowledgement> Acknowledgements { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(null);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult(_pending);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
        {
            SyncedEvents.AddRange(eventIds);
            return Task.CompletedTask;
        }

        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default)
        {
            Acknowledgements.AddRange(acknowledgements);
            return Task.CompletedTask;
        }

        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);
    }
}
