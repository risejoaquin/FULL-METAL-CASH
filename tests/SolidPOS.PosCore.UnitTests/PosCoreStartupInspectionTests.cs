using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Startup;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class PosCoreStartupInspectionTests
{
    [Fact]
    public async Task Fresh_disposable_database_is_initialized_but_never_sale_ready()
    {
        using var db = new Fixture();
        var service = new PosCoreStartupInspectionService(db.Repository);
        var status = await service.InspectAsync();
        Assert.True(status.DatabaseInitialized);
        Assert.False(status.TerminalBound);
        Assert.False(status.CanBeginAuthentication);
        Assert.False(status.CanCheckout);
        Assert.Equal("terminal_unbound", status.StatusCode);
        Assert.Equal(0, status.LocalUserCount);
        Assert.Equal(0, status.CatalogProductCount);
        Assert.True(File.Exists(db.Path));
    }

    [Fact]
    public async Task Restart_preserves_binding_and_shows_missing_real_user_without_demo_login()
    {
        using var db = new Fixture();
        var service = new PosCoreStartupInspectionService(db.Repository);
        await service.InspectAsync();
        var terminal = new TerminalBinding(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "real-test-machine", "ci-token-not-a-production-credential",
            DateTimeOffset.UtcNow, 4);
        await db.Repository.SaveTerminalBindingAsync(terminal);

        var reopened = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(db.Path));
        var restored = await new PosCoreStartupInspectionService(reopened).InspectAsync();
        Assert.True(restored.TerminalBound);
        Assert.Equal("users_missing", restored.StatusCode);
        Assert.Equal(terminal.TenantId, restored.TenantId);
        Assert.Equal(terminal.StoreId, restored.StoreId);
        Assert.Equal(terminal.TerminalId, restored.TerminalId);
        Assert.False(restored.CanBeginAuthentication);
        Assert.False(restored.CanCheckout);
    }

    [Fact]
    public async Task Terminal_bound_with_real_user_but_no_catalog_remains_blocked()
    {
        using var db = new Fixture();
        await db.Repository.InitializeAsync();
        var tenant = Guid.NewGuid();
        var store = Guid.NewGuid();
        await db.Repository.SaveTerminalBindingAsync(new TerminalBinding(
            tenant, store, Guid.NewGuid(), "device-fixture",
            "ci-only-token", DateTimeOffset.UtcNow, 4));
        await db.Repository.SaveLocalUserAsync(new LocalUser(
            Guid.NewGuid(), tenant, store, "real-user@test.invalid", "Cashier",
            "ci-only-hash", "cashier", true, DateTimeOffset.UtcNow, 12),
            ["sales.create"]);

        var status = await new PosCoreStartupInspectionService(db.Repository).InspectAsync();
        Assert.Equal("catalog_missing", status.StatusCode);
        Assert.True(status.CanBeginAuthentication);
        Assert.False(status.CanCheckout);
        Assert.True(status.LocalUsersAvailable);
    }

    [Fact]
    public async Task Wrong_terminal_schema_version_does_not_pass_activation()
    {
        using var db = new Fixture();
        await db.Repository.InitializeAsync();
        await db.Repository.SaveTerminalBindingAsync(new TerminalBinding(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "fixture-device", "ci-only-token", DateTimeOffset.UtcNow, 3));
        var status = await new PosCoreStartupInspectionService(db.Repository).InspectAsync();
        Assert.False(status.TerminalBound);
        Assert.Null(status.TenantId);
        Assert.Equal("terminal_unbound", status.StatusCode);
    }

    [Fact]
    public async Task Repeated_initialization_preserves_sqlite_state_and_wal()
    {
        using var db = new Fixture();
        var service = new PosCoreStartupInspectionService(db.Repository);
        var first = await service.InspectAsync();
        var second = await service.InspectAsync();
        Assert.Equal(first, second);
        using var conn = new SQLiteLocalDatabase(db.Path).OpenConnection();
        using var command = conn.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", command.ExecuteScalar());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "solidpos-wpf-startup-fixtures",
            Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_root, "poscore.sqlite");
        public SQLiteLocalPosRepository Repository { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Repository = new SQLiteLocalPosRepository(new SQLiteLocalDatabase(Path));
        }
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
