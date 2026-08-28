using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncOperationsRepository
{
    Task<SyncRuntimeStatusResponse> GetStatusAsync(Guid tenantId, Guid? storeId, Guid? terminalId, DateTimeOffset serverTime, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SyncDeadLetterEventResponse>> ListDeadLetterAsync(Guid tenantId, Guid? terminalId, int limit, CancellationToken cancellationToken);

    Task<RetrySyncDeadLetterResponse?> RetryDeadLetterAsync(Guid tenantId, Guid inboxEventId, string reason, CancellationToken cancellationToken);
}
