using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncPullService
{
    Task<SyncPullResponse?> PullAsync(string? cursor, int limit, CancellationToken cancellationToken);
}
