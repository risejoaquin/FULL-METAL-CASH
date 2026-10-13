using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;
using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using SolidPOS.PosCore.Infrastructure.Sync;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>
/// Real SQLite tests in disposable directories. No PostgreSQL or remote service is used.
/// </summary>
public sealed class SQLiteLocalPersistenceTests
{
    [Fact]
    public async Task InitializeAsync_is_idempotent_and_uses_WAL_and_foreign_keys()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        using var connection = fixture.Database.OpenConnection();
        using var journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string?)journalCommand.ExecuteScalar());

        using var foreignKeysCommand = connection.CreateCommand();
        foreignKeysCommand.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, (long?)foreignKeysCommand.ExecuteScalar());

        using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('terminal_binding', 'offline_sales', 'local_outbox_events', 'local_cash_shifts');
            """;
        Assert.Equal(4L, (long?)schemaCommand.ExecuteScalar());
    }

    [Fact]
    public async Task Terminal_binding_persists_across_repository_instances_in_disposable_SQLite()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var original = new TerminalBinding(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TEST-TERMINAL-001",
            "fixture-token-not-a-secret",
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero),
            4);

        var firstInstance = new SQLiteLocalPosRepository(fixture.Database);
        await firstInstance.InitializeAsync();
        await firstInstance.SaveTerminalBindingAsync(original);

        var secondInstance = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        var restored = await secondInstance.GetTerminalBindingAsync();

        Assert.NotNull(restored);
        Assert.Equal(original.TenantId, restored.TenantId);
        Assert.Equal(original.StoreId, restored.StoreId);
        Assert.Equal(original.TerminalId, restored.TerminalId);
        Assert.Equal(original.TerminalFingerprint, restored.TerminalFingerprint);
        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);

        using var connection = fixture.Database.OpenConnection();
        using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA quick_check;";
        Assert.Equal("ok", (string?)integrityCommand.ExecuteScalar());
    }


    [Fact]
    public async Task Offline_sale_and_outbox_are_committed_together_to_real_SQLite()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        DateTimeOffset at = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var sale = CreateSale(saleId, tenantId, storeId, terminalId, at);
        var message = CreateOutbox(tenantId, storeId, terminalId, at);

        await repository.SaveOfflineSaleAsync(sale, message);
        var queue = await repository.GetPendingOutboxEventsAsync(10);

        Assert.Single(queue);
        Assert.Equal(message.Id, queue[0].Id);
        Assert.Equal(4, queue[0].SchemaVersion);

        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT total_cents FROM offline_sales WHERE local_sale_id = $saleId";
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        Assert.Equal(4500L, (long?)command.ExecuteScalar());
    }

    [Fact]
    public async Task Failed_outbox_insert_rolls_back_sale_and_inventory_in_same_transaction()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        DateTimeOffset at = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var first = CreateSale(Guid.NewGuid(), tenantId, storeId, terminalId, at);
        var second = CreateSale(Guid.NewGuid(), tenantId, storeId, terminalId, at);
        var message = CreateOutbox(tenantId, storeId, terminalId, at);
        await repository.SaveOfflineSaleAsync(first, message);

        var inventoryMovement = new LocalInventoryMovement(
            Guid.NewGuid(), second.LocalSaleId, tenantId, storeId, terminalId,
            Guid.NewGuid(), null, "sale_recipe_component", -1m,
            Guid.NewGuid(), at, "test-fixture", at);

        // Reusing the outbox primary key must fail after sale+inventory inserts,
        // rolling back both of them. The original sale and message remain intact.
        await Assert.ThrowsAsync<SqliteException>(async () =>
            await repository.SaveOfflineSaleWithInventoryAsync(
                second, message, new[] { inventoryMovement }));

        using var connection = fixture.Database.OpenConnection();
        using var saleCount = connection.CreateCommand();
        saleCount.CommandText = "SELECT COUNT(*) FROM offline_sales;";
        Assert.Equal(1L, (long?)saleCount.ExecuteScalar());

        using var movementCount = connection.CreateCommand();
        movementCount.CommandText = "SELECT COUNT(*) FROM local_inventory_movements;";
        Assert.Equal(0L, (long?)movementCount.ExecuteScalar());

        using var outboxCount = connection.CreateCommand();
        outboxCount.CommandText = "SELECT COUNT(*) FROM local_outbox_events;";
        Assert.Equal(1L, (long?)outboxCount.ExecuteScalar());
    }


    [Fact]
    public async Task Transient_remote_timeout_recovers_from_SQLite_without_resending_synced_event()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var outboxEvent = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await repository.SaveOutboxEventAsync(outboxEvent);

        var client = new FailOnceRemoteSyncClient();
        var service = new RemoteSyncPushService(repository, client, new FixedClock(at));

        await Assert.ThrowsAsync<TimeoutException>(
            () => service.PushPendingAsync(10, Guid.NewGuid(), "fixture-terminal-token"));

        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.RetryPending));
        Assert.Empty(await repository.GetPendingOutboxEventsAsync(10));
        var retrying = await repository.GetLatestOutboxEventByStatusAsync(LocalOutboxStatus.RetryPending);
        Assert.NotNull(retrying);
        Assert.Equal(outboxEvent.Id, retrying.Id);
        Assert.Equal(1, retrying.Attempts);

        // Simulate the next sync cycle using a new repository instance on the same file.
        var reopened = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        Assert.Equal(1, await reopened.RecoverRetryPendingOutboxEventsAsync(5, "connection_recovered"));
        var restoredService = new RemoteSyncPushService(reopened, client, new FixedClock(at));
        var result = await restoredService.PushPendingAsync(10, Guid.NewGuid(), "fixture-terminal-token");

        Assert.NotNull(result);
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.RetryPending));

        // No pending events remain; replaying the pump cannot resend an acknowledged event.
        Assert.Null(await restoredService.PushPendingAsync(10, Guid.NewGuid(), "fixture-terminal-token"));
        Assert.Equal(2, client.CallCount);
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Permanent_remote_failure_stays_in_dead_letter_and_queue_health_requires_recovery()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var outboxEvent = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await repository.SaveOutboxEventAsync(outboxEvent);
        var service = new RemoteSyncPushService(repository, new InvalidPayloadRemoteSyncClient(), new FixedClock(at));

        await Assert.ThrowsAsync<FormatException>(
            () => service.PushPendingAsync(10, Guid.NewGuid(), "fixture-terminal-token"));

        var reopened = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        Assert.Equal(1, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.DeadLetter));
        Assert.Equal(0, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await reopened.RecoverRetryPendingOutboxEventsAsync(5, "network_recovered"));
        Assert.Equal(0, await reopened.RetryFailedOutboxEventsAsync(5, "retry_failures"));

        var latest = await reopened.GetLatestOutboxEventByStatusAsync(LocalOutboxStatus.DeadLetter);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.Attempts);
        Assert.Contains("dead_letter", latest.LastError);

        var health = await reopened.GetLocalSyncQueueHealthAsync(at.AddMinutes(1), TimeSpan.FromMinutes(5));
        Assert.Equal(1, health.DeadLetterCount);
        Assert.True(health.RequiresRecovery);
        Assert.Empty(await reopened.GetPendingOutboxEventsAsync(10));
    }

    [Fact]
    public async Task Failed_outbox_recovery_enforces_attempt_limit_and_does_not_duplicate_pending_events()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var outboxEvent = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await repository.SaveOutboxEventAsync(outboxEvent);
        await repository.MarkOutboxFailedAsync(outboxEvent.Id, "temporary_failure");
        await repository.MarkOutboxFailedAsync(outboxEvent.Id, "second_failure");

        Assert.Equal(0, await repository.RetryFailedOutboxEventsAsync(2, "retry_limit"));
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Failed));
        Assert.Equal(1, await repository.RetryFailedOutboxEventsAsync(3, "operator_approved_retry"));
        Assert.Equal(0, await repository.RetryFailedOutboxEventsAsync(3, "repeat_retry"));

        var pending = await repository.GetPendingOutboxEventsAsync(10);
        Assert.Single(pending);
        Assert.Equal(outboxEvent.Id, pending[0].Id);
        Assert.Equal(2, pending[0].Attempts);
        Assert.Equal(1, CountRows(fixture.Database, "local_outbox_events"));
    }

    private static int CountRows(SQLiteLocalDatabase database, string tableName)
    {
        // Names below are test constants, never derived from external input.
        if (tableName is not ("local_sync_acknowledgements" or "local_outbox_events" or "local_sync_claims"))
            throw new ArgumentException("Unsupported fixture table.", nameof(tableName));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FailOnceRemoteSyncClient : IRemoteSyncClient
    {
        public int CallCount { get; private set; }

        public Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
                throw new TimeoutException("fixture transient timeout");

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

    private sealed class InvalidPayloadRemoteSyncClient : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default) =>
            throw new FormatException("fixture invalid contract");
    }


    [Fact]
    public async Task Partial_HTTP_response_acknowledges_only_named_events_and_retries_rejected()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var rejected = CreateOutbox(tenantId, storeId, terminalId, now);
        var accepted = CreateOutbox(tenantId, storeId, terminalId, now) with { SequenceNumber = 2 };
        await repository.SaveOutboxEventAsync(rejected);
        await repository.SaveOutboxEventAsync(accepted);

        var json = JsonSerializer.Serialize(new
        {
            acceptedCount = 1, duplicateCount = 0, rejectedCount = 1,
            results = new[]
            {
                new { eventId = rejected.Id, status = "rejected" },
                new { eventId = accepted.Id, status = "accepted" }
            }
        });
        using var httpClient = new HttpClient(new JsonResponseHandler(json));
        var remoteClient = new HttpRemoteSyncClient(httpClient, "https://sync-fixture.invalid/");
        var service = new RemoteSyncPushService(repository, remoteClient, new FixedClock(now));

        var result = await service.PushPendingAsync(10, Guid.NewGuid(), "fixture-token");

        Assert.NotNull(result);
        Assert.Equal(new[] { accepted.Id }, result.AcknowledgedEventIds);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.RetryPending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(accepted.Id, (await repository.GetLatestOutboxEventByStatusAsync(LocalOutboxStatus.Synced))!.Id);
        Assert.Equal(rejected.Id, (await repository.GetLatestOutboxEventByStatusAsync(LocalOutboxStatus.RetryPending))!.Id);
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Partial_HTTP_response_without_event_results_cannot_guess_which_event_succeeded()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        await repository.SaveOutboxEventAsync(CreateOutbox(tenantId, storeId, terminalId, now));
        await repository.SaveOutboxEventAsync(CreateOutbox(tenantId, storeId, terminalId, now) with { SequenceNumber = 2 });

        // The aggregate counters do not tell us which event was accepted.
        const string ambiguousJson = "{\"acceptedCount\":1,\"duplicateCount\":0,\"rejectedCount\":1}";
        using var httpClient = new HttpClient(new JsonResponseHandler(ambiguousJson));
        var client = new HttpRemoteSyncClient(httpClient, "https://sync-fixture.invalid/");
        var service = new RemoteSyncPushService(repository, client, new FixedClock(now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));

        Assert.Contains("acknowledgement", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Foreign_or_duplicate_acknowledgements_cannot_mutate_SQLite_outbox()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var first = CreateOutbox(tenantId, storeId, terminalId, now);
        var second = CreateOutbox(tenantId, storeId, terminalId, now) with { SequenceNumber = 2 };
        await repository.SaveOutboxEventAsync(first);
        await repository.SaveOutboxEventAsync(second);

        var foreign = new CallbackRemoteSyncClient(request => new RemoteSyncPushResult(
            request.BatchId, 2, 1, 0, 1, "{}", new[] { Guid.NewGuid() }));
        var foreignService = new RemoteSyncPushService(repository, foreign, new FixedClock(now));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => foreignService.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));

        var duplicated = new CallbackRemoteSyncClient(request => new RemoteSyncPushResult(
            request.BatchId, 2, 2, 0, 0, "{}", new[] { first.Id, first.Id }));
        var duplicateService = new RemoteSyncPushService(repository, duplicated, new FixedClock(now));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => duplicateService.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));

        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.RetryPending));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Timeout_after_remote_commit_retries_same_ID_then_receives_duplicate_ack_once()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();

        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var message = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        await repository.SaveOutboxEventAsync(message);
        var remote = new CommitThenTimeoutRemoteSyncClient();

        await Assert.ThrowsAsync<TimeoutException>(() => new RemoteSyncPushService(
            repository, remote, new FixedClock(now)).PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));

        Assert.Equal(1, remote.BusinessSideEffects);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.RetryPending));

        // Local process restarts after remote processing but before receiving its reply.
        var restarted = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        Assert.Equal(1, await restarted.RecoverRetryPendingOutboxEventsAsync(5, "retry_after_restart"));
        var resumedService = new RemoteSyncPushService(restarted, remote, new FixedClock(now));
        var response = await resumedService.PushPendingAsync(10, Guid.NewGuid(), "fixture-token");

        Assert.NotNull(response);
        Assert.Equal(1, response.DuplicateCount);
        Assert.Equal(1, remote.BusinessSideEffects);
        Assert.Equal(2, remote.CallCount);
        Assert.Equal(1, await restarted.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await restarted.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_acknowledgements"));
        Assert.Null(await resumedService.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));
        Assert.Equal(2, remote.CallCount);
    }


    [Fact]
    public async Task Successful_remote_batch_commits_every_status_and_acknowledgement_together()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();
        DateTimeOffset at = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var first = CreateOutbox(tenantId, storeId, terminalId, at);
        var second = CreateOutbox(tenantId, storeId, terminalId, at) with { SequenceNumber = 2 };
        await repository.SaveOutboxEventAsync(first);
        await repository.SaveOutboxEventAsync(second);

        var remote = new IdempotentRemoteSyncClient();
        var result = await new RemoteSyncPushService(repository, remote, new FixedClock(at))
            .PushPendingAsync(10, Guid.NewGuid(), "fixture-token");

        Assert.NotNull(result);
        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(2, remote.BusinessSideEffects);
        var reopened = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        Assert.Equal(2, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(2, CountRows(fixture.Database, "local_sync_acknowledgements"));
        Assert.Null(await new RemoteSyncPushService(reopened, remote, new FixedClock(at))
            .PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));
        Assert.Equal(1, remote.CallCount);
    }

    [Fact]
    public async Task Failure_during_second_acknowledgement_rolls_back_entire_batch_then_replays_idempotently()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();
        DateTimeOffset at = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var first = CreateOutbox(tenantId, storeId, terminalId, at);
        var second = CreateOutbox(tenantId, storeId, terminalId, at) with { SequenceNumber = 2 };
        await repository.SaveOutboxEventAsync(first);
        await repository.SaveOutboxEventAsync(second);

        // Simulate a local disk/journal failure only on the second ACK insertion.
        // The remote has already accepted both events when this trigger fires.
        using (var connection = fixture.Database.OpenConnection())
        using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText =
                "CREATE TRIGGER fixture_fail_second_ack BEFORE INSERT ON local_sync_acknowledgements " +
                "WHEN NEW.outbox_event_id = '" + second.Id + "' " +
                "BEGIN SELECT RAISE(ABORT, 'fixture_ack_write_failed'); END;";
            trigger.ExecuteNonQuery();
        }

        var remote = new IdempotentRemoteSyncClient();
        var push = new RemoteSyncPushService(repository, remote, new FixedClock(at));
        await Assert.ThrowsAsync<SqliteException>(
            () => push.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));

        Assert.Equal(2, remote.BusinessSideEffects);
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_acknowledgements"));

        using (var connection = fixture.Database.OpenConnection())
        using (var drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP TRIGGER fixture_fail_second_ack;";
            drop.ExecuteNonQuery();
        }

        // After a local process restart, replay the exact same event IDs.
        // The simulated server recognizes both as duplicates; no new sale is made.
        var restarted = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        var recovered = await new RemoteSyncPushService(restarted, remote, new FixedClock(at))
            .PushPendingAsync(10, Guid.NewGuid(), "fixture-token");

        Assert.NotNull(recovered);
        Assert.Equal(2, recovered.DuplicateCount);
        Assert.Equal(0, recovered.AcceptedCount);
        Assert.Equal(2, remote.BusinessSideEffects);
        Assert.Equal(2, remote.CallCount);
        Assert.Equal(2, await restarted.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await restarted.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(2, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Duplicate_acknowledgement_primary_key_rolls_back_all_local_sync_transitions()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();
        DateTimeOffset at = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var first = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        var second = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await repository.SaveOutboxEventAsync(first);
        await repository.SaveOutboxEventAsync(second);

        Guid collidingAckId = Guid.NewGuid();
        Guid batchId = Guid.NewGuid();
        LocalSyncAcknowledgement[] acknowledgements =
        [
            new(collidingAckId, batchId, first.Id, "acknowledged", "{}", at),
            new(collidingAckId, batchId, second.Id, "acknowledged", "{}", at)
        ];

        await Assert.ThrowsAsync<SqliteException>(
            () => repository.CommitSyncAcknowledgementsAsync(acknowledgements, at));

        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Unknown_or_repeated_outbox_id_is_rejected_without_creating_false_sync_evidence()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();
        DateTimeOffset at = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var real = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await repository.SaveOutboxEventAsync(real);

        var invalid = new LocalSyncAcknowledgement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "acknowledged", "{}", at);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CommitSyncAcknowledgementsAsync(new[] { invalid }, at));

        var valid = new LocalSyncAcknowledgement(Guid.NewGuid(), Guid.NewGuid(), real.Id, "acknowledged", "{}", at);
        await repository.CommitSyncAcknowledgementsAsync(new[] { valid }, at);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CommitSyncAcknowledgementsAsync(new[] { valid }, at));

        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }


    [Fact]
    public async Task Two_SQLite_workers_cannot_send_the_same_pending_event_concurrently()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var workerOneRepo = new SQLiteLocalPosRepository(fixture.Database);
        await workerOneRepo.InitializeAsync();
        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var message = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await workerOneRepo.SaveOutboxEventAsync(message);

        var remote = new BlockingRemoteSyncClient();
        var first = new RemoteSyncPushService(workerOneRepo, remote, new FixedClock(at));
        var workerTwoRepo = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        var second = new RemoteSyncPushService(workerTwoRepo, remote, new FixedClock(at));

        Task<RemoteSyncPushResult?> firstRun = first.PushPendingAsync(10, Guid.NewGuid(), "fixture-token");
        try
        {
            await remote.WaitUntilEnteredAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, await workerTwoRepo.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
            Assert.Null(await second.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));
            Assert.Equal(1, remote.CallCount);
        }
        finally
        {
            remote.Unblock();
        }

        var result = await firstRun.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(result);
        Assert.Equal(1, remote.CallCount);
        Assert.Equal(1, await workerTwoRepo.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await workerTwoRepo.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_claims"));
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task SQLite_claims_split_terminal_scopes_without_cross_tenant_batches()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();
        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var first = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        var second = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await repository.SaveOutboxEventAsync(first);
        await repository.SaveOutboxEventAsync(second);

        Guid firstClaim = Guid.NewGuid();
        Guid secondClaim = Guid.NewGuid();
        var acquiredOne = await repository.ClaimPendingOutboxEventsAsync(10, firstClaim, at);
        var acquiredTwo = await new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath))
            .ClaimPendingOutboxEventsAsync(10, secondClaim, at);

        Assert.Single(acquiredOne);
        Assert.Single(acquiredTwo);
        Assert.NotEqual(acquiredOne[0].Id, acquiredTwo[0].Id);
        Assert.True(new HashSet<Guid> { first.Id, second.Id }.SetEquals(acquiredOne.Concat(acquiredTwo).Select(e => e.Id)));
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(2, CountRows(fixture.Database, "local_sync_claims"));

        await repository.ReleaseClaimAsync(firstClaim);
        await repository.ReleaseClaimAsync(secondClaim);
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_claims"));
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    [Fact]
    public async Task Expired_claim_can_be_recovered_but_stale_worker_cannot_commit_or_release_new_owner()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var initial = new SQLiteLocalPosRepository(fixture.Database);
        await initial.InitializeAsync();
        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var message = CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at);
        await initial.SaveOutboxEventAsync(message);

        Guid staleClaimId = Guid.NewGuid();
        Assert.Single(await initial.ClaimPendingOutboxEventsAsync(10, staleClaimId, at));
        var current = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(fixture.DatabasePath));
        Assert.Equal(0, await current.RecoverStaleClaimsAsync(at.AddMinutes(4), TimeSpan.FromMinutes(5)));
        Assert.Equal(1, await current.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));

        Assert.Equal(1, await current.RecoverStaleClaimsAsync(at.AddMinutes(6), TimeSpan.FromMinutes(5)));
        Assert.Equal(1, await current.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Guid currentClaimId = Guid.NewGuid();
        Assert.Single(await current.ClaimPendingOutboxEventsAsync(10, currentClaimId, at.AddMinutes(6)));

        var staleAck = new LocalSyncAcknowledgement(Guid.NewGuid(), staleClaimId, message.Id, "acknowledged", "{}", at);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => initial.CommitClaimedSyncAcknowledgementsAsync(staleClaimId, new[] { staleAck }, at));
        await initial.ReleaseClaimAsync(staleClaimId);

        Assert.Equal(1, await current.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_claims"));
        var currentAck = new LocalSyncAcknowledgement(Guid.NewGuid(), currentClaimId, message.Id, "acknowledged", "{}", at);
        await current.CommitClaimedSyncAcknowledgementsAsync(currentClaimId, new[] { currentAck }, at);
        Assert.Equal(1, await initial.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_claims"));
        Assert.Equal(1, CountRows(fixture.Database, "local_sync_acknowledgements"));
    }

    [Fact]
    public async Task Malformed_remote_ACK_releases_claim_without_losing_pending_event()
    {
        using var fixture = new DisposableSQLiteDatabase();
        var repository = new SQLiteLocalPosRepository(fixture.Database);
        await repository.InitializeAsync();
        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        await repository.SaveOutboxEventAsync(CreateOutbox(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), at));

        var badClient = new CallbackRemoteSyncClient(request => new RemoteSyncPushResult(
            request.BatchId, request.Events.Count, 1, 0, 0, "{}", new[] { Guid.NewGuid() }));
        var service = new RemoteSyncPushService(repository, badClient, new FixedClock(at));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PushPendingAsync(10, Guid.NewGuid(), "fixture-token"));
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, CountRows(fixture.Database, "local_sync_claims"));
    }

    private sealed class BlockingRemoteSyncClient : IRemoteSyncClient
    {
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public Task WaitUntilEnteredAsync() => _entered.Task;
        public void Unblock() => _resume.TrySetResult(true);

        public async Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            _entered.TrySetResult(true);
            await _resume.Task.WaitAsync(cancellationToken);
            return new RemoteSyncPushResult(
                request.BatchId, request.Events.Count, request.Events.Count, 0, 0, "{}",
                request.Events.Select(item => item.EventId).ToArray());
        }
    }

    private sealed class IdempotentRemoteSyncClient : IRemoteSyncClient
    {
        private readonly HashSet<Guid> _acceptedEventIds = new();
        public int CallCount { get; private set; }
        public int BusinessSideEffects { get; private set; }

        public Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            int accepted = 0;
            int duplicated = 0;
            foreach (var item in request.Events)
            {
                if (_acceptedEventIds.Add(item.EventId))
                {
                    BusinessSideEffects++;
                    accepted++;
                }
                else
                {
                    duplicated++;
                }
            }
            return Task.FromResult(new RemoteSyncPushResult(
                request.BatchId, request.Events.Count, accepted, duplicated, 0,
                "{\"fixture\":true}", request.Events.Select(item => item.EventId).ToArray()));
        }
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class CallbackRemoteSyncClient(
        Func<RemoteSyncPushRequest, RemoteSyncPushResult> callback) : IRemoteSyncClient
    {
        public Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(callback(request));
    }

    private sealed class CommitThenTimeoutRemoteSyncClient : IRemoteSyncClient
    {
        private readonly HashSet<Guid> _committedEventIds = new();
        public int CallCount { get; private set; }
        public int BusinessSideEffects { get; private set; }

        public Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var accepted = 0;
            var duplicates = 0;
            foreach (var item in request.Events)
            {
                if (_committedEventIds.Add(item.EventId))
                {
                    accepted++;
                    BusinessSideEffects++;
                }
                else
                {
                    duplicates++;
                }
            }

            if (CallCount == 1)
            {
                throw new TimeoutException("fixture connection dropped after server commit");
            }

            return Task.FromResult(new RemoteSyncPushResult(
                request.BatchId, request.Events.Count, accepted, duplicates, 0,
                "{\"simulated\":true}", request.Events.Select(item => item.EventId).ToArray()));
        }
    }

    private static OfflineSaleDraft CreateSale(
        Guid localSaleId, Guid tenantId, Guid storeId, Guid terminalId, DateTimeOffset at) =>
        new(localSaleId, tenantId, storeId, terminalId, at,
            new[] { new OfflineSaleLineDraft(Guid.NewGuid(), null, "TEST-001", "Test product", 1, 4500) },
            new[] { new OfflineSalePaymentDraft("cash", 4500) }, "MXN");

    private static LocalOutboxEvent CreateOutbox(
        Guid tenantId, Guid storeId, Guid terminalId, DateTimeOffset at) =>
        new(Guid.NewGuid(), tenantId, storeId, terminalId, "sale.completed",
            4, 1, "{\"fixture\":true}", LocalOutboxStatus.Pending, at);

    private sealed class DisposableSQLiteDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "solidpos-sqlite-tests", Guid.NewGuid().ToString("N"));

        public DisposableSQLiteDatabase()
        {
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "pos-test.sqlite");
            Database = new SQLiteLocalDatabase(DatabasePath);
        }

        public string DatabasePath { get; }
        public SQLiteLocalDatabase Database { get; }

        public void Dispose()
        {
            // Microsoft.Data.Sqlite uses connection pooling by default. Even disposed
            // connections can keep the DB/WAL files locked on GitHub's Windows runner.
            // This fixture owns only disposable files and never touches real data.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
