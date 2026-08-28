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
    public async Task PushPendingAsync_marks_duplicate_acknowledgement_as_synced()
    {
        var eventId = Guid.NewGuid();
        var outboxEvent = CreateHealthCheckEvent(eventId);
        var repository = new InMemoryLocalPosRepository(new[] { outboxEvent });
        var client = new DuplicateRemoteSyncClient();
        var service = new RemoteSyncPushService(repository, client, new FixedClock());

        var result = await service.PushPendingAsync(500, Guid.NewGuid(), "terminal-token");

        Assert.NotNull(result);
        Assert.Equal(1, result!.DuplicateCount);
        Assert.Contains(eventId, repository.SyncedEvents);
        Assert.Single(repository.Acknowledgements);
    }

    [Fact]
    public async Task PushPendingAsync_marks_pending_events_failed_when_remote_call_fails()
    {
        var eventId = Guid.NewGuid();
        var outboxEvent = CreateHealthCheckEvent(eventId);
        var repository = new InMemoryLocalPosRepository(new[] { outboxEvent });
        var service = new RemoteSyncPushService(repository, new FailingRemoteSyncClient(), new FixedClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PushPendingAsync(500, Guid.NewGuid(), "terminal-token"));

        Assert.Contains(eventId, repository.FailedEvents);
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


    private static LocalOutboxEvent CreateHealthCheckEvent(Guid eventId)
    {
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        return new LocalOutboxEvent(
            eventId,
            tenantId,
            storeId,
            terminalId,
            "pos.health_check",
            4,
            100,
            $"{{\"terminalId\":\"{terminalId}\"}}",
            LocalOutboxStatus.Pending,
            DateTimeOffset.UtcNow);
    }

    private sealed class DuplicateRemoteSyncClient : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RemoteSyncPushResult(
                request.BatchId,
                request.Events.Count,
                0,
                request.Events.Count,
                0,
                "{\"acceptedCount\":0,\"duplicateCount\":1,\"rejectedCount\":0}",
                request.Events.Select(item => item.EventId).ToArray()));
        }
    }

    private sealed class FailingRemoteSyncClient : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("remote temporarily unavailable");
        }
    }

    private sealed class InMemoryLocalPosRepository : ILocalPosRepository
    {
        private readonly IReadOnlyList<LocalOutboxEvent> _pending;

        public InMemoryLocalPosRepository(IReadOnlyList<LocalOutboxEvent> pending) => _pending = pending;

        public List<Guid> SyncedEvents { get; } = new();
        public List<Guid> FailedEvents { get; } = new();
        public List<LocalSyncAcknowledgement> Acknowledgements { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(null);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult<LocalCatalogProduct?>(null);

        public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default) => SaveOfflineSaleAsync(sale, outboxEvent, cancellationToken);
        public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default) => Task.FromResult<LocalInventoryRecipe?>(null);
        public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(Array.Empty<LocalInventoryRecipeItem>());
        public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(Array.Empty<LocalInventoryMovement>());
        public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult(_pending);
        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult<LocalOutboxEvent?>(null);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
        {
            SyncedEvents.AddRange(eventIds);
            return Task.CompletedTask;
        }

        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
        {
            FailedEvents.Add(eventId);
            return Task.CompletedTask;
        }

        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default) => Task.FromResult(0);
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
