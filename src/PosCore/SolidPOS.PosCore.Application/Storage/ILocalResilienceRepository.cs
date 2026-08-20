using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Storage;

public interface ILocalResilienceRepository
{
    Task<LocalIntegrityReport> VerifyIntegrityAsync(CancellationToken cancellationToken = default);
    Task<LocalRecoveryResult> RepairRuntimeAsync(string reason, bool createBackup, CancellationToken cancellationToken = default);
    Task<LocalBackupResult> CreateBackupAsync(string destinationDirectory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalRecoveryJournalEntry>> GetRecoveryJournalAsync(int limit, CancellationToken cancellationToken = default);
}
