using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncPushService
{
    Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken cancellationToken);
}
