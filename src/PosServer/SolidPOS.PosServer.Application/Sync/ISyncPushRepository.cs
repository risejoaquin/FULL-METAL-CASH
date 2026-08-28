using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncPushRepository
{
    Task<IReadOnlyCollection<SyncPushEventResultResponse>> IngestAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid batchId,
        IReadOnlyCollection<SyncPushEventEnvelope> events,
        CancellationToken cancellationToken);
}
