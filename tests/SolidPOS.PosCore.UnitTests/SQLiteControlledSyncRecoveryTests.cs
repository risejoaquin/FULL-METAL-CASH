using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>
/// Controlled operator recoveries use only disposable SQLite files.
/// Approval references are fixture values, not real approvals or privileges.
/// </summary>
public sealed class SQLiteControlledSyncRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Scoped_dry_run_reports_candidates_and_audits_without_releasing_claim()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        Guid foreignTenant = Guid.NewGuid(), foreignStore = Guid.NewGuid(), foreignTerminal = Guid.NewGuid();
        var target = NewEvent(tenant, store, terminal, sequence: 1);
        var foreign = NewEvent(foreignTenant, foreignStore, foreignTerminal, sequence: 100);
        await repository.SaveOutboxEventAsync(target);
        await repository.SaveOutboxEventAsync(foreign);
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), Now.AddMinutes(-6)));
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), Now.AddMinutes(-6)));

        var request = await fixture.AuthorizeAsync(Request(tenant, store, terminal, dryRun: true));
        var output = await fixture.NewRepository().RecoverStaleClaimsWithAuditAsync(request);
        Assert.True(output.DryRun);
        Assert.Equal(1, output.EligibleCount);
        Assert.Equal(0, output.RecoveredCount);
        Assert.Equal(new[] { target.Id }, output.CandidateEventIds);
        Assert.False(output.HasMoreEligible);
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(2, fixture.Count("local_sync_claims"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_candidates"));
        Assert.Equal(1, fixture.ScalarInt(
            "SELECT dry_run FROM local_sync_recovery_runs LIMIT 1"));
        Assert.Equal(0, fixture.ScalarInt(
            "SELECT recovered_count FROM local_sync_recovery_runs LIMIT 1"));
    }

    [Fact]
    public async Task Live_recovery_requires_approval_and_only_mutates_requested_terminal()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid();
        Guid targetTerminal = Guid.NewGuid(), otherTerminal = Guid.NewGuid();
        var target = NewEvent(tenant, store, targetTerminal, sequence: 1);
        var other = NewEvent(tenant, store, otherTerminal, sequence: 100);
        await repository.SaveOutboxEventAsync(target);
        await repository.SaveOutboxEventAsync(other);
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), Now.AddMinutes(-10)));
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), Now.AddMinutes(-1)));

        var unauthorized = Request(tenant, store, targetTerminal, dryRun: false) with { ApprovalReference = null };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.RecoverStaleClaimsWithAuditAsync(unauthorized));
        Assert.Equal(0, fixture.Count("local_sync_recovery_runs"));

        var approved = await fixture.AuthorizeAsync(Request(tenant, store, targetTerminal, dryRun: false), approve: true);
        var result = await repository.RecoverStaleClaimsWithAuditAsync(approved);
        Assert.Equal(1, result.EligibleCount);
        Assert.Equal(1, result.RecoveredCount);
        Assert.Equal(new[] { target.Id }, result.CandidateEventIds);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(1, fixture.Count("local_sync_claims"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_candidates"));
        Assert.Equal(1, fixture.ScalarInt(
            "SELECT recovered_count FROM local_sync_recovery_runs LIMIT 1"));
        Assert.Equal(0, fixture.ScalarInt(
            "SELECT dry_run FROM local_sync_recovery_runs LIMIT 1"));
        Assert.Equal("fixture-approval-001", fixture.ScalarString(
            "SELECT approval_reference FROM local_sync_recovery_runs LIMIT 1"));
    }

    [Fact]
    public async Task Live_recovery_limits_each_batch_and_records_follow_up_without_duplicates()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        for (int i = 0; i < 3; i++)
        {
            await repository.SaveOutboxEventAsync(NewEvent(tenant, store, terminal, i + 1));
            Assert.Single(await repository.ClaimPendingOutboxEventsAsync(1, Guid.NewGuid(), Now.AddMinutes(-10)));
        }

        var firstRequest = await fixture.AuthorizeAsync(
            Request(tenant, store, terminal, dryRun: false) with { MaxEvents = 2 }, approve: true);
        var first = await repository.RecoverStaleClaimsWithAuditAsync(firstRequest);
        Assert.Equal(3, first.EligibleCount);
        Assert.Equal(2, first.RecoveredCount);
        Assert.True(first.HasMoreEligible);
        Assert.Equal(2, first.CandidateEventIds.Count);
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));

        var reopened = fixture.NewRepository();
        var secondRequest = await fixture.AuthorizeAsync(
            Request(tenant, store, terminal, dryRun: false) with { MaxEvents = 2 }, approve: true);
        var second = await reopened.RecoverStaleClaimsWithAuditAsync(secondRequest);
        Assert.Equal(1, second.EligibleCount);
        Assert.Equal(1, second.RecoveredCount);
        Assert.False(second.HasMoreEligible);
        Assert.Equal(3, await reopened.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, fixture.Count("local_sync_claims"));
        Assert.Equal(2, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(3, fixture.Count("local_sync_recovery_candidates"));
        Assert.Equal(3, fixture.ScalarInt(
            "SELECT SUM(recovered_count) FROM local_sync_recovery_runs"));
    }

    [Fact]
    public async Task Duplicate_recovery_run_id_rolls_back_without_additional_state_changes()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        var evt = NewEvent(tenant, store, terminal);
        await repository.SaveOutboxEventAsync(evt);
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(1, Guid.NewGuid(), Now.AddMinutes(-6)));

        var request = await fixture.AuthorizeAsync(Request(tenant, store, terminal, dryRun: true));
        await repository.RecoverStaleClaimsWithAuditAsync(request);
        await Assert.ThrowsAsync<SqliteException>(
            () => repository.RecoverStaleClaimsWithAuditAsync(
                request with { DryRun = false, ApprovalReference = "fixture-approval-001" }));

        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, fixture.Count("local_sync_claims"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(1, fixture.Count("local_sync_recovery_candidates"));
    }

    [Fact]
    public async Task Audit_insert_failure_rolls_back_event_recovery_and_claim_removal()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
        var evt = NewEvent(tenant, store, terminal);
        await repository.SaveOutboxEventAsync(evt);
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, Guid.NewGuid(), Now.AddMinutes(-6)));

        var authorized = await fixture.AuthorizeAsync(Request(tenant, store, terminal, dryRun: false), approve: true);
        fixture.Execute("""
CREATE TRIGGER fixture_block_recovery_audit
BEFORE INSERT ON local_sync_recovery_candidates
BEGIN SELECT RAISE(ABORT, 'fixture_audit_failed'); END;
""");
        try
        {
            await Assert.ThrowsAsync<SqliteException>(
                () => repository.RecoverStaleClaimsWithAuditAsync(authorized));
        }
        finally
        {
            fixture.Execute("DROP TRIGGER fixture_block_recovery_audit;");
        }

        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, fixture.Count("local_sync_claims"));
        Assert.Equal(0, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(0, fixture.Count("local_sync_recovery_candidates"));

        var result = await repository.RecoverStaleClaimsWithAuditAsync(authorized);
        Assert.Equal(1, result.RecoveredCount);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    [Fact]
    public async Task Invalid_timeout_scope_and_limit_are_rejected_before_any_SQLite_mutation()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.NewRepository();
        await repository.InitializeAsync();
        var valid = Request(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), dryRun: false);
        LocalSyncRecoveryRequest[] invalid =
        [
            valid with { TenantId = Guid.Empty },
            valid with { StoreId = Guid.Empty },
            valid with { TerminalId = Guid.Empty },
            valid with { MaxEvents = 0 },
            valid with { MaxEvents = 101 },
            valid with { ProcessingTimeout = TimeSpan.FromSeconds(1) },
            valid with { ProcessingTimeout = TimeSpan.FromDays(2) },
            valid with { Reason = "" },
            valid with { Actor = "" }
        ];
        foreach (var candidate in invalid)
            await Assert.ThrowsAnyAsync<ArgumentException>(
                () => repository.RecoverStaleClaimsWithAuditAsync(candidate));

        Assert.Equal(0, fixture.Count("local_sync_recovery_runs"));
        Assert.Equal(0, fixture.Count("local_sync_recovery_candidates"));
    }

    private static LocalSyncRecoveryRequest Request(
        Guid tenant, Guid store, Guid terminal, bool dryRun) =>
        new(Guid.NewGuid(), tenant, store, terminal, Now, Timeout, 10,
            "fixture-operator", "fixture recovery after explicit review", dryRun,
            dryRun ? null : "fixture-approval-001");

    private static LocalOutboxEvent NewEvent(
        Guid tenant, Guid store, Guid terminal, long sequence = 1) =>
        new(Guid.NewGuid(), tenant, store, terminal, "sale.completed", 4,
            sequence, "{\"fixture\":true}", LocalOutboxStatus.Pending, Now.AddHours(-1));

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "solidpos-controlled-recovery-tests", Guid.NewGuid().ToString("N"));

        public DatabaseFixture()
        {
            Directory.CreateDirectory(_directory);
            Database = new SQLiteLocalDatabase(Path.Combine(_directory, "controlled.sqlite"));
        }

        private SQLiteLocalDatabase Database { get; }
        public SQLiteLocalPosRepository NewRepository() => new(Database);

        public async Task<LocalSyncRecoveryRequest> AuthorizeAsync(
            LocalSyncRecoveryRequest request, bool approve = false, bool sameUser = false,
            bool omitExecutePermission = false, bool omitApprovePermission = false,
            bool expiredExecutor = false, bool differentStore = false)
        {
            var repository = NewRepository();
            Guid executorId = Guid.NewGuid(), approverId = sameUser ? executorId : Guid.NewGuid();
            Guid executorSessionId = Guid.NewGuid(), approverSessionId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            Guid executorStore = differentStore ? Guid.NewGuid() : request.StoreId;
            var executor = new LocalUser(executorId, request.TenantId, executorStore,
                $"executor-{executorId:N}@fixture.invalid", "Executor", "fixture-password-not-used",
                "operator", true, now, 12);
            await repository.SaveLocalUserAsync(executor,
                omitExecutePermission ? [] : [LocalSyncRecoveryPermissions.Execute]);
            await repository.CreateLocalSessionAsync(new LocalSession(
                executorSessionId, executorId, request.TenantId, executorStore, executor.Email,
                executor.DisplayName, executor.RoleCode, now.AddMinutes(-5),
                expiredExecutor ? now.AddMinutes(-1) : now.AddMinutes(30), "active"));

            if (approve)
            {
                if (!sameUser)
                {
                    var approver = new LocalUser(approverId, request.TenantId, request.StoreId,
                        $"approver-{approverId:N}@fixture.invalid", "Approver",
                        "fixture-password-not-used", "supervisor", true, now, 12);
                    await repository.SaveLocalUserAsync(approver,
                        omitApprovePermission ? [] : [LocalSyncRecoveryPermissions.Approve]);
                }
                else
                {
                    await repository.SaveLocalUserAsync(executor,
                        omitApprovePermission ? [LocalSyncRecoveryPermissions.Execute]
                            : [LocalSyncRecoveryPermissions.Execute, LocalSyncRecoveryPermissions.Approve]);
                }

                await repository.CreateLocalSessionAsync(new LocalSession(
                    approverSessionId, approverId, request.TenantId, request.StoreId,
                    $"approver-{approverId:N}@fixture.invalid", "Approver", "supervisor",
                    now.AddMinutes(-5), now.AddMinutes(30), "active"));
                await repository.RegisterRecoveryApprovalAsync(new LocalSyncRecoveryApprovalRequest(
                    request.RecoveryId, request.TenantId, request.StoreId, request.TerminalId,
                    approverSessionId, request.ProcessingTimeout, request.MaxEvents,
                    request.Reason, request.ApprovalReference!));
            }

            return request with { ExecutorSessionId = executorSessionId };
        }

        public int Count(string table)
        {
            if (table is not ("local_sync_recovery_runs" or "local_sync_recovery_candidates" or "local_sync_claims"))
                throw new ArgumentException("Unexpected fixture table.", nameof(table));
            return ScalarInt($"SELECT COUNT(*) FROM {table};");
        }

        public int ScalarInt(string sql)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public string ScalarString(string sql)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (string)command.ExecuteScalar()!;
        }

        public void Execute(string sql)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
