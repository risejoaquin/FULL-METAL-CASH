using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>
/// Fake verified authority (NO real server) plus real SQLite and password-based
/// local session. Tests the post-authentication trust boundary, not HTTP/JWT.
/// </summary>
public sealed class SQLiteAuthoritativePermissionRefreshTests
{
    [Fact]
    public async Task Updated_remote_grant_atomically_revokes_old_session_without_overwriting_password()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var session = await fx.LoginAsync();
        Assert.True(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
        var original = await fx.Repository.GetLocalUserByEmailAsync(fx.Email);
        var authority = fx.Source(1, true, "manager", "sync.recovery.execute", "sales.read");
        await fx.Service(authority).RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId);

        Assert.True(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.execute"));
        Assert.False(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
        Assert.Equal("closed", (await fx.Repository.GetLocalSessionAsync(session.SessionId))!.Status);
        var updated = await fx.Repository.GetLocalUserByEmailAsync(fx.Email);
        Assert.Equal(original!.PasswordHash, updated!.PasswordHash);
        Assert.Equal("manager", updated.RoleCode);
        Assert.Equal(1, fx.Scalar("SELECT authority_version FROM local_authority_snapshot_state;"));
        Assert.Equal(1, fx.Scalar("SELECT COUNT(*) FROM local_audit_events WHERE event_type='local.auth.authority_changed_sessions_closed';"));
        var reauthenticated = await fx.LoginAsync();
        await new LocalAuthService(fx.Repository, new SystemClock()).RequirePermissionAsync(
            reauthenticated.SessionId, "sync.recovery.execute");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
                Guid.NewGuid(), fx.TenantId, fx.StoreId, fx.TerminalId, session.SessionId,
                TimeSpan.FromMinutes(5), 10, "no stale approvals", "fixture-ref")));
    }

    [Fact]
    public async Task Replay_of_older_authority_version_does_not_restore_revoked_permission()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var authority = fx.Source(2, true, "cashier", "sales.read");
        await fx.Service(authority).RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId);
        authority.Snapshot = authority.Snapshot with
        {
            AuthorityVersion = 1,
            EffectivePermissions = ["sync.recovery.approve"]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fx.Service(authority).RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId));
        Assert.False(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
        Assert.Equal(2, fx.Scalar("SELECT authority_version FROM local_authority_snapshot_state;"));
        Assert.Equal(1, fx.Scalar("SELECT COUNT(*) FROM local_authority_snapshot_state;"));
    }

    [Fact]
    public async Task Mismatched_authority_scope_and_terminal_binding_fail_closed_without_evidence()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var wrongSource = fx.Source(1, true, "owner", "sync.recovery.execute");
        wrongSource.Snapshot = wrongSource.Snapshot with { StoreId = Guid.NewGuid() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service(wrongSource).RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId));

        var wrongBinding = fx.Source(1, true, "owner", "sync.recovery.execute");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service(wrongBinding).RefreshAsync(fx.TenantId, fx.StoreId, Guid.NewGuid(), fx.UserId));
        Assert.Equal(0, fx.Scalar("SELECT COUNT(*) FROM local_authority_snapshot_state;"));
        Assert.True(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
    }

    [Fact]
    public async Task Future_or_old_snapshot_and_invalid_grants_are_rejected_before_SQLite()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var source = fx.Source(1, true, "operator", "sales.read");
        var service = fx.Service(source);
        foreach (var invalid in new[]
        {
            source.Snapshot with { IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(1) },
            source.Snapshot with { IssuedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) },
            source.Snapshot with { EffectivePermissions = ["sync.recovery.approve", "sync.recovery.approve"] },
            source.Snapshot with { EffectivePermissions = ["SYNC.RECOVERY.APPROVE"] },
            source.Snapshot with { AuthorityVersion = 0 },
            source.Snapshot with { IsActive = false, EffectivePermissions = ["sync.recovery.execute"] }
        })
        {
            source.Snapshot = invalid;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId));
        }
        Assert.Equal(0, fx.Scalar("SELECT COUNT(*) FROM local_authority_snapshot_state;"));
        Assert.True(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
    }

    [Fact]
    public async Task SQLite_audit_failure_rolls_back_new_grants_version_and_existing_sessions()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var priorSession = await fx.LoginAsync();
        fx.Execute("""
CREATE TRIGGER test_rollback_auth BEFORE INSERT ON local_audit_events
WHEN NEW.event_type='local.auth.authority_changed_sessions_closed'
BEGIN SELECT RAISE(ABORT,'test_audit_fault'); END;
""");
        try
        {
            await Assert.ThrowsAsync<SqliteException>(() => fx.Service(
                fx.Source(3, true, "manager", "sync.recovery.execute"))
                .RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId));
        }
        finally { fx.Execute("DROP TRIGGER test_rollback_auth;"); }

        Assert.True(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
        Assert.False(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.execute"));
        Assert.Equal("active", (await fx.Repository.GetLocalSessionAsync(priorSession.SessionId))!.Status);
        Assert.Equal(0, fx.Scalar("SELECT COUNT(*) FROM local_authority_snapshot_state;"));
        await fx.Service(fx.Source(3, true, "manager", "sync.recovery.execute"))
            .RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId);
        Assert.Equal("closed", (await fx.Repository.GetLocalSessionAsync(priorSession.SessionId))!.Status);
    }

    [Fact]
    public async Task Same_grants_newer_version_refreshes_cache_without_closing_unchanged_session()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var session = await fx.LoginAsync();
        await fx.Service(fx.Source(4, true, "owner", "sync.recovery.approve"))
            .RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId);
        Assert.Equal("active", (await fx.Repository.GetLocalSessionAsync(session.SessionId))!.Status);
        Assert.Equal(4, fx.Scalar("SELECT authority_version FROM local_authority_snapshot_state;"));
        Assert.Equal(1, fx.Scalar("SELECT COUNT(*) FROM local_audit_events WHERE event_type='local.auth.authority_refreshed';"));
    }

    [Fact]
    public async Task Inactive_authority_user_clears_all_grants_and_prevents_local_login()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var session = await fx.LoginAsync();
        await fx.Service(fx.Source(5, false, "owner"))
            .RefreshAsync(fx.TenantId, fx.StoreId, fx.TerminalId, fx.UserId);
        Assert.False(await fx.Repository.LocalUserHasPermissionAsync(fx.UserId, "sync.recovery.approve"));
        Assert.Equal("closed", (await fx.Repository.GetLocalSessionAsync(session.SessionId))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fx.LoginAsync());
    }

    private sealed class MockAuthority(VerifiedPermissionSnapshot snapshot) : IVerifiedPermissionSnapshotSource
    {
        public VerifiedPermissionSnapshot Snapshot { get; set; } = snapshot;
        public Task<VerifiedPermissionSnapshot> FetchVerifiedAsync(
            Guid tenantId, Guid storeId, Guid terminalId, Guid userId,
            CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(),
            "solidpos-role-refresh-ci", Guid.NewGuid().ToString("N"));
        private readonly SQLiteLocalDatabase _database;
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid StoreId = Guid.NewGuid();
        public readonly Guid TerminalId = Guid.NewGuid();
        public readonly Guid UserId = Guid.NewGuid();
        public string Email => "fixture-authorized-user@local.invalid";
        public SQLiteLocalPosRepository Repository { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_folder);
            _database = new SQLiteLocalDatabase(Path.Combine(_folder, "auth.sqlite"));
            Repository = new SQLiteLocalPosRepository(_database);
        }
        public async Task InitializeAsync()
        {
            await Repository.InitializeAsync();
            await Repository.SaveTerminalBindingAsync(new TerminalBinding(
                TenantId, StoreId, TerminalId, "fixture-machine", "fixture-token",
                DateTimeOffset.UtcNow, 4));
            await Repository.SaveLocalUserAsync(new LocalUser(UserId, TenantId, StoreId,
                Email, "Fixture Operator", LocalPasswordHasher.Hash("secure-fixture-password"),
                "owner", true, DateTimeOffset.UtcNow, 12), ["sync.recovery.approve"]);
        }

        public Task<LocalSession> LoginAsync() =>
            new LocalAuthService(Repository, new SystemClock())
                .LoginAsync(Email, "secure-fixture-password");

        public MockAuthority Source(long version, bool active, string role, params string[] permissions) =>
            new(new VerifiedPermissionSnapshot(TenantId, StoreId, TerminalId, UserId,
                version, DateTimeOffset.UtcNow, active, role, permissions));

        public AuthoritativePermissionRefreshService Service(MockAuthority source) =>
            new(source, Repository, new SystemClock());

        public int Scalar(string query)
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = query;
            return Convert.ToInt32(command.ExecuteScalar());
        }
        public void Execute(string sql)
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }
    }
}
