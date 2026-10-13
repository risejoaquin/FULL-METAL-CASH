using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Sync;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>
/// Simulated process restarts use fresh repositories against the same disposable
/// SQLite file. All server side effects occur in-memory, never over a network.
/// </summary>
public sealed class SQLiteSyncRestartRecoveryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Two_abandoned_restarts_replay_one_event_without_repeating_business_effect()
    {
        using var fixture = new DatabaseFixture();
        var initial = fixture.OpenRepository();
        await initial.InitializeAsync();
        var evt = NewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sequence: 1);
        await initial.SaveOutboxEventAsync(evt);

        var server = new DeduplicatingRemoteServer();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var now = Epoch.AddMinutes(10 * cycle);
            var restarted = fixture.OpenRepository();
            if (cycle != 0)
                Assert.Equal(1, await restarted.RecoverStaleClaimsAsync(now, ClaimTimeout));

            Guid abandonedClaim = Guid.NewGuid();
            var batch = await restarted.ClaimPendingOutboxEventsAsync(10, abandonedClaim, now);
            Assert.Single(batch);
            Assert.Equal(evt.Id, batch[0].Id);

            var request = RemoteSyncPushMapper.Map(LocalOutboxBatchPlanner.CreateBatch(abandonedClaim, batch));
            var response = await server.PushAsync(request, "fake-token");
            Assert.Equal(cycle == 0 ? 1 : 0, response.AcceptedCount);
            Assert.Equal(cycle == 0 ? 0 : 1, response.DuplicateCount);
            // Intentionally omit both the ACK journal commit and release; process crashed.
        }

        var recoveredRepo = fixture.OpenRepository();
        var lastRestart = Epoch.AddMinutes(20);
        Assert.Equal(1, await recoveredRepo.RecoverStaleClaimsAsync(lastRestart, ClaimTimeout));
        var service = new RemoteSyncPushService(recoveredRepo, server, new TestClock(lastRestart));
        var final = await service.PushPendingAsync(10, Guid.NewGuid(), "fake-token");

        Assert.NotNull(final);
        Assert.Equal(1, final.DuplicateCount);
        Assert.Equal(1, server.BusinessEffects);
        Assert.Equal(3, server.Calls);
        Assert.Single(server.SeenIds);
        Assert.Contains(evt.Id, server.SeenIds);
        Assert.Equal(1, await recoveredRepo.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(0, await recoveredRepo.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, await recoveredRepo.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, fixture.CountRows("local_sync_acknowledgements"));
        Assert.Equal(0, fixture.CountRows("local_sync_claims"));
        Assert.Null(await service.PushPendingAsync(10, Guid.NewGuid(), "fake-token"));
        Assert.Equal(3, server.Calls);
    }

    [Fact]
    public async Task Abandoned_terminal_A_restarts_while_terminal_B_finishes_without_cross_scope_effects()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.OpenRepository();
        await repository.InitializeAsync();
        var tenantA = Guid.NewGuid();
        var storeA = Guid.NewGuid();
        var terminalA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var terminalB = Guid.NewGuid();
        var a1 = NewEvent(tenantA, storeA, terminalA, 1);
        var a2 = NewEvent(tenantA, storeA, terminalA, 2);
        var b1 = NewEvent(tenantB, storeB, terminalB, 100);
        var b2 = NewEvent(tenantB, storeB, terminalB, 101);
        foreach (var evt in new[] { a1, b1, a2, b2 })
            await repository.SaveOutboxEventAsync(evt);

        var server = new DeduplicatingRemoteServer();
        Guid claimA = Guid.NewGuid();
        Guid claimB = Guid.NewGuid();
        var claimedA = await fixture.OpenRepository().ClaimPendingOutboxEventsAsync(10, claimA, Epoch);
        var claimedB = await fixture.OpenRepository().ClaimPendingOutboxEventsAsync(10, claimB, Epoch);

        Assert.True(new HashSet<Guid> { a1.Id, a2.Id }.SetEquals(claimedA.Select(e => e.Id)));
        Assert.True(new HashSet<Guid> { b1.Id, b2.Id }.SetEquals(claimedB.Select(e => e.Id)));

        var remoteA = await server.PushAsync(
            RemoteSyncPushMapper.Map(LocalOutboxBatchPlanner.CreateBatch(claimA, claimedA)), "fake-token");
        var remoteB = await server.PushAsync(
            RemoteSyncPushMapper.Map(LocalOutboxBatchPlanner.CreateBatch(claimB, claimedB)), "fake-token");
        Assert.Equal(2, remoteA.AcceptedCount);
        Assert.Equal(2, remoteB.AcceptedCount);

        // Terminal B commits normally; terminal A loses its ACK after remote success.
        await fixture.OpenRepository().CommitClaimedSyncAcknowledgementsAsync(
            claimB, claimedB.Select(e => new LocalSyncAcknowledgement(
                Guid.NewGuid(), claimB, e.Id, "acknowledged", "{}", Epoch)).ToArray(), Epoch);

        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
        Assert.Equal(2, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(2, fixture.CountRows("local_sync_acknowledgements"));

        var recovered = fixture.OpenRepository();
        Assert.Equal(2, await recovered.RecoverStaleClaimsAsync(Epoch.AddMinutes(6), ClaimTimeout));
        Assert.Equal(2, await recovered.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(2, await recovered.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));

        var result = await new RemoteSyncPushService(recovered, server, new TestClock(Epoch.AddMinutes(6)))
            .PushPendingAsync(10, Guid.NewGuid(), "fake-token");
        Assert.NotNull(result);
        Assert.Equal(2, result.DuplicateCount);
        Assert.Equal(4, server.BusinessEffects);
        Assert.Equal(3, server.Calls);
        Assert.Equal(4, fixture.CountRows("local_sync_acknowledgements"));
        Assert.Equal(0, fixture.CountRows("local_sync_claims"));
        Assert.Equal(4, await recovered.CountOutboxByStatusAsync(LocalOutboxStatus.Synced));
    }

    [Fact]
    public async Task Stale_claim_recovery_is_idempotent_and_preserves_other_healthy_terminal()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.OpenRepository();
        await repository.InitializeAsync();
        var old = NewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        var newer = NewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);
        await repository.SaveOutboxEventAsync(old);
        await repository.SaveOutboxEventAsync(newer);

        var firstClaim = Guid.NewGuid();
        var secondClaim = Guid.NewGuid();
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(1, firstClaim, Epoch));
        Assert.Single(await fixture.OpenRepository().ClaimPendingOutboxEventsAsync(
            1, secondClaim, Epoch.AddMinutes(4)));

        var fresh = fixture.OpenRepository();
        Assert.Equal(1, await fresh.RecoverStaleClaimsAsync(Epoch.AddMinutes(5), ClaimTimeout));
        Assert.Equal(0, await fresh.RecoverStaleClaimsAsync(Epoch.AddMinutes(5), ClaimTimeout));
        Assert.Equal(1, await fresh.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(1, await fresh.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(1, fixture.CountRows("local_sync_claims"));

        Assert.Equal(1, await fresh.RecoverStaleClaimsAsync(Epoch.AddMinutes(9), ClaimTimeout));
        Assert.Equal(0, await fresh.RecoverStaleClaimsAsync(Epoch.AddMinutes(9), ClaimTimeout));
        Assert.Equal(2, await fresh.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
        Assert.Equal(0, await fresh.CountOutboxByStatusAsync(LocalOutboxStatus.InFlight));
        Assert.Equal(0, fixture.CountRows("local_sync_claims"));
        Assert.Equal(2, fixture.CountRows("local_sync_claim_history"));
    }

    [Fact]
    public async Task Expired_claim_identifier_cannot_be_reissued_to_another_worker()
    {
        using var fixture = new DatabaseFixture();
        var repository = fixture.OpenRepository();
        await repository.InitializeAsync();
        var evt = NewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        await repository.SaveOutboxEventAsync(evt);

        Guid expiredClaim = Guid.NewGuid();
        Assert.Single(await repository.ClaimPendingOutboxEventsAsync(10, expiredClaim, Epoch));
        Assert.Equal(1, await fixture.OpenRepository().RecoverStaleClaimsAsync(
            Epoch.AddMinutes(6), ClaimTimeout));

        var restarted = fixture.OpenRepository();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => restarted.ClaimPendingOutboxEventsAsync(10, expiredClaim, Epoch.AddMinutes(6)));
        Assert.Contains("already used", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await restarted.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));

        Guid freshClaim = Guid.NewGuid();
        Assert.Single(await restarted.ClaimPendingOutboxEventsAsync(10, freshClaim, Epoch.AddMinutes(6)));
        Assert.Equal(2, fixture.CountRows("local_sync_claim_history"));
        Assert.Equal(1, fixture.CountRows("local_sync_claims"));
        await restarted.ReleaseClaimAsync(freshClaim);
    }

    [Fact]
    public async Task Existing_active_claim_is_imported_into_history_during_SQLite_schema_initialization()
    {
        using var fixture = new DatabaseFixture();
        Guid historicClaim = Guid.NewGuid();
        using (var connection = fixture.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
CREATE TABLE local_sync_claims (
  outbox_event_id TEXT PRIMARY KEY,
  claim_id TEXT NOT NULL,
  claimed_at_utc TEXT NOT NULL
);
INSERT INTO local_sync_claims (outbox_event_id, claim_id, claimed_at_utc)
VALUES ($event, $claim, $at);
""";
            command.Parameters.AddWithValue("$event", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$claim", historicClaim.ToString());
            command.Parameters.AddWithValue("$at", Epoch.ToString("O"));
            command.ExecuteNonQuery();
        }

        var repository = fixture.OpenRepository();
        await repository.InitializeAsync();
        await repository.InitializeAsync(); // Migration remains idempotent.
        Assert.Equal(1, fixture.CountRows("local_sync_claim_history"));

        var newEvent = NewEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        await repository.SaveOutboxEventAsync(newEvent);
        Assert.Equal(0, await repository.RecoverStaleClaimsAsync(Epoch.AddMinutes(6), ClaimTimeout));
        Assert.Equal(0, fixture.CountRows("local_sync_claims"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.ClaimPendingOutboxEventsAsync(10, historicClaim, Epoch.AddMinutes(6)));
        Assert.Contains("already used", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await repository.CountOutboxByStatusAsync(LocalOutboxStatus.Pending));
    }

    private static LocalOutboxEvent NewEvent(Guid tenantId, Guid storeId, Guid terminalId, long sequence) =>
        new(Guid.NewGuid(), tenantId, storeId, terminalId, "sale.completed", 4, sequence,
            "{\"fixture\":true}", LocalOutboxStatus.Pending, Epoch);

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class DeduplicatingRemoteServer : IRemoteSyncClient
    {
        private readonly HashSet<Guid> _seen = new();
        public int Calls { get; private set; }
        public int BusinessEffects { get; private set; }
        public IReadOnlyCollection<Guid> SeenIds => _seen;

        public Task<RemoteSyncPushResult> PushAsync(
            RemoteSyncPushRequest request, string terminalAccessToken,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var accepted = 0;
            var duplicated = 0;
            foreach (var evt in request.Events)
            {
                if (_seen.Add(evt.EventId))
                {
                    accepted++;
                    BusinessEffects++;
                }
                else
                {
                    duplicated++;
                }
            }
            return Task.FromResult(new RemoteSyncPushResult(
                request.BatchId, request.Events.Count, accepted, duplicated, 0,
                "{\"fixture\":true}", request.Events.Select(x => x.EventId).ToArray()));
        }
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "solidpos-restart-tests", Guid.NewGuid().ToString("N"));

        public DatabaseFixture()
        {
            Directory.CreateDirectory(_directory);
            Database = new SQLiteLocalDatabase(Path.Combine(_directory, "restarts.sqlite"));
        }

        public SQLiteLocalDatabase Database { get; }
        public SQLiteLocalPosRepository OpenRepository() =>
            new(new SQLiteLocalDatabase(Database.DatabasePath));

        public int CountRows(string table)
        {
            if (table is not ("local_sync_acknowledgements" or "local_sync_claims" or "local_sync_claim_history"))
                throw new ArgumentException("Unexpected fixture table.", nameof(table));
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
