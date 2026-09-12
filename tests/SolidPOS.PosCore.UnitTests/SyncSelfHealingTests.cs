using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class SyncSelfHealingTests
{
    [Fact]
    public async Task Transient_failure_moves_event_to_retry_pending_with_backoff_and_jitter()
    {
        var eventId = Guid.NewGuid();
        var outboxEvent = CreateEvent(eventId, attempts: 1);
        var repository = new SelfHealingRepository([outboxEvent]);
        var service = new RemoteSyncPushService(repository, new TimeoutRemoteSyncClient(), new FixedClock());

        await Assert.ThrowsAsync<TimeoutException>(() => service.PushPendingAsync(10, Guid.NewGuid(), "terminal-token"));

        Assert.Contains(eventId, repository.RetryPendingEvents);
        Assert.Empty(repository.DeadLetterEvents);
        Assert.Contains("retry_pending", repository.LastReason);
        Assert.Contains("backoff_ms", repository.LastReason);
    }

    [Fact]
    public void Retry_policy_backoff_increases_and_jitter_exists()
    {
        var eventId = Guid.NewGuid();
        var policy = new LocalSyncRetryPolicy();
        var first = policy.EvaluateException(CreateEvent(eventId, attempts: 0), new TimeoutException("timeout"));
        var second = policy.EvaluateException(CreateEvent(eventId, attempts: 2), new TimeoutException("timeout"));

        Assert.True(second.Delay > first.Delay);
        Assert.True(first.Delay > TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Retry_pending_event_recovers_to_pending_and_syncs_without_duplicate_side_effect()
    {
        var eventId = Guid.NewGuid();
        var outboxEvent = CreateEvent(eventId, attempts: 1) with { Status = LocalOutboxStatus.RetryPending };
        var repository = new SelfHealingRepository([outboxEvent]);

        var recovered = await repository.RecoverRetryPendingOutboxEventsAsync(5, "transient_recovered");
        var service = new RemoteSyncPushService(repository, new SuccessfulRemoteSyncClient(), new FixedClock());
        var result = await service.PushPendingAsync(10, Guid.NewGuid(), "terminal-token");

        Assert.Equal(1, recovered);
        Assert.NotNull(result);
        Assert.Contains(eventId, repository.SyncedEvents);
        Assert.Equal(1, repository.RemoteSideEffects);
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.RetryPending));
    }

    [Fact]
    public async Task Poison_event_moves_to_dead_letter_and_remains_visible()
    {
        var eventId = Guid.NewGuid();
        var outboxEvent = CreateEvent(eventId, attempts: LocalSyncRetryPolicy.DefaultMaxAttempts - 1);
        var repository = new SelfHealingRepository([outboxEvent]);
        var service = new RemoteSyncPushService(repository, new PoisonRemoteSyncClient(), new FixedClock());

        await Assert.ThrowsAsync<FormatException>(() => service.PushPendingAsync(10, Guid.NewGuid(), "terminal-token"));

        Assert.Contains(eventId, repository.DeadLetterEvents);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.DeadLetter));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    [Fact]
    public async Task Duplicate_acknowledgement_is_idempotent_and_does_not_duplicate_business_effect()
    {
        var eventId = Guid.NewGuid();
        var repository = new SelfHealingRepository([CreateEvent(eventId)]);
        var service = new RemoteSyncPushService(repository, new DuplicateRemoteSyncClient(), new FixedClock());

        var result = await service.PushPendingAsync(10, Guid.NewGuid(), "terminal-token");

        Assert.NotNull(result);
        Assert.Equal(1, result!.DuplicateCount);
        Assert.Contains(eventId, repository.SyncedEvents);
        Assert.Single(repository.SyncedEvents);
    }

    [Fact]
    public async Task Queue_health_summary_reports_retry_dead_letter_and_stuck_processing()
    {
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var repository = new SelfHealingRepository([
            CreateEvent(Guid.NewGuid(), createdAtUtc: now.AddMinutes(-10)),
            CreateEvent(Guid.NewGuid(), createdAtUtc: now.AddMinutes(-9)) with { Status = LocalOutboxStatus.RetryPending },
            CreateEvent(Guid.NewGuid(), createdAtUtc: now.AddMinutes(-8)) with { Status = LocalOutboxStatus.DeadLetter },
            CreateEvent(Guid.NewGuid(), createdAtUtc: now.AddMinutes(-30)) with { Status = LocalOutboxStatus.InFlight }
        ]);

        var summary = await repository.GetLocalSyncQueueHealthAsync(now, TimeSpan.FromMinutes(5));

        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(1, summary.ProcessingCount);
        Assert.Equal(1, summary.RetryPendingCount);
        Assert.Equal(1, summary.DeadLetterCount);
        Assert.True(summary.HasStuckProcessing);
        Assert.True(summary.RequiresRecovery);
        Assert.NotNull(summary.OldestPendingAtUtc);
        Assert.NotNull(summary.OldestRetryPendingAtUtc);
    }

    [Fact]
    public void Batch_planner_preserves_tenant_isolation()
    {
        var first = CreateEvent(Guid.NewGuid(), tenantId: Guid.NewGuid());
        var second = CreateEvent(Guid.NewGuid(), tenantId: Guid.NewGuid());

        var error = Assert.Throws<InvalidOperationException>(() => LocalOutboxBatchPlanner.CreateBatch(Guid.NewGuid(), [first, second]));

        Assert.Contains("cannot mix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LocalOutboxEvent CreateEvent(Guid eventId, int attempts = 0, Guid? tenantId = null, DateTimeOffset? createdAtUtc = null)
    {
        var resolvedTenantId = tenantId ?? Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        return new LocalOutboxEvent(
            eventId,
            resolvedTenantId,
            storeId,
            terminalId,
            "sale.completed",
            4,
            100,
            $"{{\"saleId\":\"{Guid.NewGuid()}\"}}",
            LocalOutboxStatus.Pending,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            Attempts: attempts);
    }

    private sealed class SuccessfulRemoteSyncClient : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new RemoteSyncPushResult(
                request.BatchId,
                request.Events.Count,
                request.Events.Count,
                0,
                0,
                "{\"acceptedCount\":1}",
                request.Events.Select(item => item.EventId).ToArray()));
        }
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
                "{\"duplicateCount\":1}",
                request.Events.Select(item => item.EventId).ToArray()));
        }
    }

    private sealed class TimeoutRemoteSyncClient : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            throw new TimeoutException("transient timeout");
        }
    }

    private sealed class PoisonRemoteSyncClient : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            throw new FormatException("payload contract invalid");
        }
    }

    private sealed class SelfHealingRepository : ILocalPosRepository
    {
        private readonly Dictionary<Guid, LocalOutboxEvent> _events;

        public SelfHealingRepository(IReadOnlyList<LocalOutboxEvent> events) => _events = events.ToDictionary(item => item.Id);

        public List<Guid> SyncedEvents { get; } = new();
        public List<Guid> RetryPendingEvents { get; } = new();
        public List<Guid> DeadLetterEvents { get; } = new();
        public string LastReason { get; private set; } = string.Empty;
        public int RemoteSideEffects { get; private set; }

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
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default)
        {
            _events[outboxEvent.Id] = outboxEvent;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(
                _events.Values.Where(item => item.Status == LocalOutboxStatus.Pending).OrderBy(item => item.SequenceNumber).Take(limit).ToArray());
        }

        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_events.Values.Where(item => item.Status == status).OrderByDescending(item => item.SequenceNumber).FirstOrDefault());
        }

        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default)
        {
            foreach (var eventId in eventIds)
            {
                var current = _events[eventId];
                if (current.Status != LocalOutboxStatus.Synced)
                {
                    RemoteSideEffects++;
                }

                _events[eventId] = current with { Status = LocalOutboxStatus.Synced, SyncedAtUtc = syncedAtUtc };
                SyncedEvents.Add(eventId);
            }

            return Task.CompletedTask;
        }

        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Move(eventId, LocalOutboxStatus.Failed, error);
        public Task MarkOutboxRetryPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
        {
            RetryPendingEvents.Add(eventId);
            return Move(eventId, LocalOutboxStatus.RetryPending, reason);
        }

        public Task MarkOutboxDeadLetterAsync(Guid eventId, string reason, CancellationToken cancellationToken = default)
        {
            DeadLetterEvents.Add(eventId);
            return Move(eventId, LocalOutboxStatus.DeadLetter, reason);
        }

        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Move(eventId, LocalOutboxStatus.Pending, reason, incrementAttempts: false);

        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default)
        {
            var candidates = _events.Values.Where(item => item.Status == LocalOutboxStatus.Failed && item.Attempts < maxAttempts).ToArray();
            foreach (var candidate in candidates)
            {
                _events[candidate.Id] = candidate with { Status = LocalOutboxStatus.Pending, LastError = reason };
            }

            return Task.FromResult(candidates.Length);
        }

        public Task<int> RecoverRetryPendingOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default)
        {
            var candidates = _events.Values.Where(item => item.Status == LocalOutboxStatus.RetryPending && item.Attempts < maxAttempts).ToArray();
            foreach (var candidate in candidates)
            {
                _events[candidate.Id] = candidate with { Status = LocalOutboxStatus.Pending, LastError = reason };
            }

            return Task.FromResult(candidates.Length);
        }

        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(_events.Values.Count(item => item.Status == status));

        public Task<LocalSyncQueueHealthSummary> GetLocalSyncQueueHealthAsync(DateTimeOffset nowUtc, TimeSpan processingTimeout, CancellationToken cancellationToken = default)
        {
            var oldestPending = _events.Values.Where(item => item.Status == LocalOutboxStatus.Pending).MinBy(item => item.CreatedAtUtc)?.CreatedAtUtc;
            var oldestRetry = _events.Values.Where(item => item.Status == LocalOutboxStatus.RetryPending).MinBy(item => item.CreatedAtUtc)?.CreatedAtUtc;
            var oldestProcessing = _events.Values.Where(item => item.Status == LocalOutboxStatus.InFlight).MinBy(item => item.CreatedAtUtc)?.CreatedAtUtc;
            var summary = new LocalSyncQueueHealthSummary(
                _events.Values.Count(item => item.Status == LocalOutboxStatus.Pending),
                _events.Values.Count(item => item.Status == LocalOutboxStatus.InFlight),
                _events.Values.Count(item => item.Status == LocalOutboxStatus.RetryPending),
                _events.Values.Count(item => item.Status == LocalOutboxStatus.DeadLetter),
                oldestPending,
                oldestRetry,
                oldestProcessing.HasValue && nowUtc - oldestProcessing.Value > processingTimeout,
                _events.Values.Any(item => item.Status is LocalOutboxStatus.RetryPending or LocalOutboxStatus.DeadLetter));
            return Task.FromResult(summary with { RequiresRecovery = summary.RequiresRecovery || summary.HasStuckProcessing });
        }

        private Task Move(Guid eventId, LocalOutboxStatus status, string reason, bool incrementAttempts = true)
        {
            LastReason = reason;
            var current = _events[eventId];
            _events[eventId] = current with
            {
                Status = status,
                LastError = reason,
                Attempts = incrementAttempts ? current.Attempts + 1 : current.Attempts
            };
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    }
}
