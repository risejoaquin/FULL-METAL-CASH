using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
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
        if (tableName is not ("local_sync_acknowledgements" or "local_outbox_events"))
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
