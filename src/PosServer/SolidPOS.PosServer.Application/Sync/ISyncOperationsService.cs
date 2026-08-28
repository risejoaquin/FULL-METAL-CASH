using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncOperationsService
{
    Task<SyncRuntimeStatusResponse?> GetStatusAsync(Guid? storeId, Guid? terminalId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SyncDeadLetterEventResponse>?> ListDeadLetterAsync(Guid? terminalId, int limit, CancellationToken cancellationToken);

    Task<RetrySyncDeadLetterResponse?> RetryDeadLetterAsync(Guid inboxEventId, RetrySyncDeadLetterRequest request, CancellationToken cancellationToken);

    SyncContractResponse GetContract();
}
