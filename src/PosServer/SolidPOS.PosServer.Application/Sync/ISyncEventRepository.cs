using System.Text.Json;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncEventRepository
{
    Task<IReadOnlyCollection<SyncInboxEvent>> ReadPendingAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid? batchId,
        int maxEvents,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(
        Guid tenantId,
        Guid inboxEventId,
        JsonElement result,
        CancellationToken cancellationToken);

    Task MarkRejectedAsync(
        Guid tenantId,
        Guid inboxEventId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);

    Task MarkConflictAsync(
        Guid tenantId,
        Guid inboxEventId,
        Guid conflictId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}
