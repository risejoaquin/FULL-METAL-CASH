using Microsoft.Data.Sqlite;
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
