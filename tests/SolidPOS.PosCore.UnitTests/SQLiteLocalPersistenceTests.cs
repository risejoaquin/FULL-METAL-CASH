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
