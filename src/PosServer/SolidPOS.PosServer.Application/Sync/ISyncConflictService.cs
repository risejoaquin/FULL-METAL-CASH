using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncConflictService
{
    Task<IReadOnlyCollection<SyncConflictResponse>?> ListAsync(string? status, int limit, CancellationToken cancellationToken);

    Task<SyncConflictResponse?> ResolveAsync(Guid conflictId, ResolveSyncConflictRequest request, CancellationToken cancellationToken);

    Task<SyncBootstrapResponse?> BootstrapAsync(CancellationToken cancellationToken);
}
