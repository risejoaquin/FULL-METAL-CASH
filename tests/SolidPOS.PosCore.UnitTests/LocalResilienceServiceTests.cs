using SolidPOS.PosCore.Application.Resilience;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class LocalResilienceServiceTests
{
    [Fact]
    public async Task RequireHealthy_allows_ok_report()
    {
        var repository = new FakeResilienceRepository(new LocalIntegrityReport(
            "local.sqlite",
            true,
            "ok",
            0,
            0,
            0,
            0,
            0,
            1,
            1,
            0,
            Array.Empty<LocalIntegrityIssue>()));

        var service = new LocalResilienceService(repository);

        var report = await service.RequireHealthyAsync();

        Assert.True(report.IsOk);
    }

    [Fact]
    public async Task RequireHealthy_blocks_critical_integrity_report()
    {
        var repository = new FakeResilienceRepository(new LocalIntegrityReport(
            "local.sqlite",
            false,
            "ok",
            0,
            0,
            1,
            0,
            0,
            1,
            1,
            0,
            new[] { new LocalIntegrityIssue("outbox.dead_letter", "critical", "dead letter exists", 1) }));

        var service = new LocalResilienceService(repository);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequireHealthyAsync());

        Assert.Contains("outbox.dead_letter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repair_delegates_backup_and_counts()
    {
        var repository = new FakeResilienceRepository(new LocalIntegrityReport(
            "local.sqlite", true, "ok", 0, 0, 0, 0, 0, 1, 1, 0, Array.Empty<LocalIntegrityIssue>()));
        var service = new LocalResilienceService(repository);

        var result = await service.RepairAsync("test", createBackup: true);

        Assert.True(result.BackupCreated);
        Assert.Equal(2, result.OutboxEventsRepaired);
        Assert.Equal(1, result.PrintJobsRepaired);
    }

    private sealed class FakeResilienceRepository : ILocalResilienceRepository
    {
        private readonly LocalIntegrityReport _report;

        public FakeResilienceRepository(LocalIntegrityReport report)
        {
            _report = report;
        }

        public Task<LocalIntegrityReport> VerifyIntegrityAsync(CancellationToken cancellationToken = default) => Task.FromResult(_report);

        public Task<LocalRecoveryResult> RepairRuntimeAsync(string reason, bool createBackup, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalRecoveryResult(Guid.NewGuid(), 2, 1, 0, 0, createBackup, createBackup ? "backup.sqlite" : null, reason));

        public Task<LocalBackupResult> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalBackupResult(Path.Combine(destinationDirectory, "backup.sqlite"), 100, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<LocalRecoveryJournalEntry>> GetRecoveryJournalAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalRecoveryJournalEntry>>(Array.Empty<LocalRecoveryJournalEntry>());
    }
}
