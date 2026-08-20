using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Resilience;

public sealed class LocalResilienceService
{
    private readonly ILocalResilienceRepository _repository;

    public LocalResilienceService(ILocalResilienceRepository repository)
    {
        _repository = repository;
    }

    public Task<LocalIntegrityReport> VerifyAsync(CancellationToken cancellationToken = default)
        => _repository.VerifyIntegrityAsync(cancellationToken);

    public async Task<LocalIntegrityReport> RequireHealthyAsync(CancellationToken cancellationToken = default)
    {
        var report = await _repository.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
        if (!report.IsOk)
        {
            var issues = string.Join(" | ", report.Issues.Select(x => $"{x.Code}:{x.Count}"));
            throw new InvalidOperationException($"Local database integrity check failed. issues={issues}");
        }

        return report;
    }

    public Task<LocalRecoveryResult> RepairAsync(string reason, bool createBackup, CancellationToken cancellationToken = default)
        => _repository.RepairRuntimeAsync(reason, createBackup, cancellationToken);

    public Task<LocalBackupResult> BackupAsync(string destinationDirectory, CancellationToken cancellationToken = default)
        => _repository.CreateBackupAsync(destinationDirectory, cancellationToken);

    public Task<IReadOnlyList<LocalRecoveryJournalEntry>> JournalAsync(int limit, CancellationToken cancellationToken = default)
        => _repository.GetRecoveryJournalAsync(limit, cancellationToken);
}
