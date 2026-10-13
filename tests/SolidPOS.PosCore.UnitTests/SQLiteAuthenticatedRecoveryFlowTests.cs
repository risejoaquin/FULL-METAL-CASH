using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>
/// Uses the real offline credential verifier and the actual SQLite session /
/// permission tables. Only disposable local data is created. The central
/// Postgres role service remains unconnected and must be tested separately.
/// </summary>
public sealed class SQLiteAuthenticatedRecoveryFlowTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Password_authenticated_executor_and_independent_reviewer_can_recover_one_stale_event()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        var evt = NewEvent(tenant, store, terminal, now);
        await repository.SaveOutboxEventAsync(evt);
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), now.AddMinutes(-6)));

        var executor = await fixture.LoginAsync(tenant, store, "executor",
            LocalSyncRecoveryPermissions.Execute, now);
        var reviewer = await fixture.LoginAsync(tenant, store, "reviewer",
            LocalSyncRecoveryPermissions.Approve, now);
        var auth = new LocalAuthService(repository, new WallClock());
        await auth.RequirePermissionAsync(executor.SessionId, LocalSyncRecoveryPermissions.Execute);
        await auth.RequirePermissionAsync(reviewer.SessionId, LocalSyncRecoveryPermissions.Approve);

        Guid recoveryId = Guid.NewGuid();
        var reason = "supervised fixture claim recovery";
        await repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
            recoveryId, tenant, store, terminal, reviewer.SessionId,
            Timeout, 10, reason, "fixture-ticket-accepted"));

        var result = await repository.RecoverStaleClaimsWithAuditAsync(new LocalSyncRecoveryRequest(
            recoveryId, tenant, store, terminal, now, Timeout, 10,
            executor.Email, reason, DryRun: false,
            ApprovalReference: "fixture-ticket-accepted",
            ExecutorSessionId: executor.SessionId));

        Assert.Equal(1, result.RecoveredCount);
        Assert.Equal(new[] { evt.Id }, result.CandidateEventIds);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(1, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_executors"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_approvals"));
        Assert.Equal(executor.SessionId.ToString(),
            fixture.ScalarString("SELECT executor_session_id FROM local_sync_recovery_executors LIMIT 1"));
        Assert.Equal(reviewer.UserId.ToString(),
            fixture.ScalarString("SELECT approver_user_id FROM local_sync_recovery_approvals LIMIT 1"));
        Assert.Equal(1, fixture.ScalarInt(
            "SELECT COUNT(*) FROM local_sync_recovery_approvals WHERE consumed_at_utc IS NOT NULL"));
    }

    [Fact]
    public async Task Cached_permissions_expiring_after_login_invalidates_recovery_even_with_active_session()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        await repository.SaveOutboxEventAsync(NewEvent(tenant, store, terminal, now));
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), now.AddMinutes(-6)));

        var executor = await fixture.LoginAsync(tenant, store, "executor",
            LocalSyncRecoveryPermissions.Execute, now);
        var reviewer = await fixture.LoginAsync(tenant, store, "reviewer",
            LocalSyncRecoveryPermissions.Approve, now);
        Guid recoveryId = Guid.NewGuid();
        await repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
            recoveryId, tenant, store, terminal, reviewer.SessionId,
            Timeout, 10, "expiry fixture", "fixture-expiry"));

        // The authenticated session remains open; its central-authority cache is stale.
        fixture.Execute($"UPDATE local_users SET last_synced_at_utc = '{now.AddHours(-13):O}' WHERE user_id = '{executor.UserId}';");
        var request = new LocalSyncRecoveryRequest(
            recoveryId, tenant, store, terminal, now, Timeout, 10,
            executor.Email, "expiry fixture", false, "fixture-expiry",
            executor.SessionId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => repository.RecoverStaleClaimsWithAuditAsync(request));

        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(1, fixture.ScalarInt(
            "SELECT COUNT(*) FROM local_sync_recovery_approvals WHERE consumed_at_utc IS NULL"));
    }

    [Fact]
    public async Task Revoked_reviewers_cached_role_cannot_authorize_after_login()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        var reviewer = await fixture.LoginAsync(tenant, store, "reviewer",
            LocalSyncRecoveryPermissions.Approve, now);

        // The reviewer logged in successfully, but the cached role was revoked.
        await repository.SaveLocalUserAsync(new LocalUser(
            reviewer.UserId, tenant, store, reviewer.Email, "reviewer",
            LocalPasswordHasher.Hash("fixture-password-123"), "owner", true,
            now, 12), Array.Empty<string>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
                Guid.NewGuid(), tenant, store, terminal, reviewer.SessionId,
                Timeout, 10, "revoked fixture", "fixture-revoked")));
        Assert.Equal(0, fixture.Count("local_sync_recovery_approvals"));
    }

    [Fact]
    public async Task Genuine_other_store_login_cannot_approve_or_execute_for_target_store()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), otherStore = Guid.NewGuid(), terminal = Guid.NewGuid();
        var other = await fixture.LoginAsync(tenant, otherStore, "other-store",
            LocalSyncRecoveryPermissions.Approve, now);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
                Guid.NewGuid(), tenant, store, terminal, other.SessionId,
                Timeout, 10, "foreign store fixture", "fixture-other-store")));
        Assert.Equal(0, fixture.Count("local_sync_recovery_approvals"));

        var executor = await fixture.LoginAsync(tenant, otherStore, "other-executor",
            LocalSyncRecoveryPermissions.Execute, now);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => repository.RecoverStaleClaimsWithAuditAsync(new LocalSyncRecoveryRequest(
                Guid.NewGuid(), tenant, store, terminal, now, Timeout, 10,
                executor.Email, "foreign store fixture", true,
                ExecutorSessionId: executor.SessionId)));
        Assert.Equal(0, fixture.Count("local_sync_recovery_runs"));
    }

    [Fact]
    public async Task Owner_label_and_standard_sync_permission_never_imply_recovery_approval()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        var user = await fixture.LoginAsync(tenant, store, "owner",
            "sync.push", now, role: "owner");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
                Guid.NewGuid(), tenant, store, terminal, user.SessionId,
                Timeout, 10, "owner not privileged fixture", "fixture-no-auto-grant")));
        Assert.Equal(0, fixture.Count("local_sync_recovery_approvals"));
    }

    private static LocalOutboxEvent NewEvent(
        Guid tenant, Guid store, Guid terminal, DateTimeOffset now) =>
        new(Guid.NewGuid(), tenant, store, terminal, "sale.completed",
            4, 1, "{\"fixture\":true}", LocalOutboxStatus.Pending, now.AddHours(-1));

    private sealed class WallClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "solidpos-real-auth-recovery", Guid.NewGuid().ToString("N"));
        private readonly SQLiteLocalDatabase _database;

        public DatabaseFixture()
        {
            Directory.CreateDirectory(_dir);
            _database = new SQLiteLocalDatabase(Path.Combine(_dir, "local.sqlite"));
        }

        public SQLiteLocalPosRepository NewRepository() => new(_database);

        public async Task<LocalSession> LoginAsync(
            Guid tenant, Guid store, string label, string permission,
            DateTimeOffset now, string role = "operator")
        {
            var repo = NewRepository();
            var email = $"{label}-{Guid.NewGuid():N}@fixture.invalid";
            var user = new LocalUser(
                Guid.NewGuid(), tenant, store, email, label,
                LocalPasswordHasher.Hash("fixture-password-123"),
                role, true, now, 12);
            await repo.SaveLocalUserAsync(user, [permission]);
            var service = new LocalAuthService(repo, new WallClock());
            var session = await service.LoginAsync(email, "fixture-password-123");
            Assert.Equal(user.UserId, session.UserId);
            Assert.Equal(role, session.RoleCode);
            return session;
        }

        public int Count(string table)
        {
            if (table is not ("local_sync_recovery_runs" or "local_sync_recovery_executors"
                or "local_sync_recovery_approvals"))
                throw new ArgumentException("Unexpected fixture table.", nameof(table));
            return ScalarInt($"SELECT COUNT(*) FROM {table};");
        }

        public int ScalarInt(string sql)
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public string ScalarString(string sql)
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (string)command.ExecuteScalar()!;
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
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
    }
}
