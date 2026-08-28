using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class LocalSyncPullServiceTests
{
    [Fact]
    public async Task PullAndApplyAsync_applies_changes_and_updates_cursor()
    {
        var repository = new CapturingPullRepository(new LocalSyncPullState(null, null, 0, 0));
        var client = new FakePullClient(new[] { CreateChange("tenant.catalog"), CreateChange("terminal.runtime") });
        var service = new LocalSyncPullService(repository, client, new FixedClock());

        var result = await service.PullAndApplyAsync(100, "terminal-token");

        Assert.Equal(2, result.ReceivedChangeCount);
        Assert.Equal(2, result.AppliedChangeCount);
        Assert.Equal("2026-08-19T10:00:00.0000000+00:00", repository.LastCursor);
        Assert.Equal(2, repository.AppliedChanges.Count);
    }

    [Fact]
    public async Task PullAndApplyAsync_tracks_duplicate_applied_changes()
    {
        var change = CreateChange("tenant.catalog");
        var repository = new CapturingPullRepository(new LocalSyncPullState("cursor-1", DateTimeOffset.UtcNow, 1, 1))
        {
            ForceAppliedCount = 0
        };
        var client = new FakePullClient(new[] { change });
        var service = new LocalSyncPullService(repository, client, new FixedClock());

        var result = await service.PullAndApplyAsync(100, "terminal-token");

        Assert.Equal(1, result.ReceivedChangeCount);
        Assert.Equal(0, result.AppliedChangeCount);
        Assert.Equal(1, result.SkippedDuplicateCount);
        Assert.Equal("cursor-1", client.CursorSeen);
    }

    private static RemoteSyncPullChange CreateChange(string entityType)
    {
        var entityId = Guid.NewGuid();
        return new RemoteSyncPullChange(
            Guid.NewGuid(),
            entityType,
            entityId,
            "snapshot",
            1,
            new DateTimeOffset(2026, 8, 19, 9, 59, 0, TimeSpan.Zero),
            JsonSerializer.SerializeToElement(new { id = entityId, name = entityType }),
            null,
            null);
    }

    private sealed class FakePullClient : IRemoteSyncPullClient
    {
        private readonly IReadOnlyList<RemoteSyncPullChange> _changes;

        public FakePullClient(IReadOnlyList<RemoteSyncPullChange> changes) => _changes = changes;

        public string? CursorSeen { get; private set; }

        public Task<RemoteSyncPullResponse> PullAsync(string? cursor, int limit, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            CursorSeen = cursor;
            return Task.FromResult(new RemoteSyncPullResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
                cursor,
                "2026-08-19T10:00:00.0000000+00:00",
                false,
                _changes,
                "{}"));
        }
    }

    private sealed class CapturingPullRepository : ILocalPosRepository
    {
        private readonly LocalSyncPullState _state;

        public CapturingPullRepository(LocalSyncPullState state) => _state = state;

        public int? ForceAppliedCount { get; init; }
        public List<LocalAppliedSyncChange> AppliedChanges { get; } = new();
        public string? LastCursor { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(null);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default) => Task.FromResult<LocalInventoryRecipe?>(null);
        public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(Array.Empty<LocalInventoryRecipeItem>());
        public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(Array.Empty<LocalInventoryMovement>());
        public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult<LocalCatalogProduct?>(null);
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(Array.Empty<LocalOutboxEvent>());
        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult<LocalOutboxEvent?>(null);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<LocalSyncPullState> GetSyncPullStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task<int> ApplySyncPullChangesAsync(IReadOnlyCollection<LocalAppliedSyncChange> changes, string nextCursor, DateTimeOffset pulledAtUtc, CancellationToken cancellationToken = default)
        {
            AppliedChanges.AddRange(changes);
            LastCursor = nextCursor;
            return Task.FromResult(ForceAppliedCount ?? changes.Count);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 19, 10, 0, 1, TimeSpan.Zero);
    }
}
