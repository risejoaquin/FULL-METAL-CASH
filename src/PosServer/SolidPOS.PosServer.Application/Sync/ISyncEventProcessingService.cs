using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncEventProcessingService
{
    Task<SyncProcessResponse?> ProcessPendingAsync(SyncProcessRequest request, CancellationToken cancellationToken);
}
