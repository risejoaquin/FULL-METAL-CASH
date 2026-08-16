using System.Text.Json;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncConflictRepository
{
    Task<IReadOnlyCollection<SyncConflictResponse>> ListAsync(
        Guid tenantId,
        string? status,
        int limit,
        CancellationToken cancellationToken);

    Task<SyncConflictResponse?> ResolveAsync(
        Guid tenantId,
        Guid conflictId,
        Guid? resolvedByUserId,
        ResolveSyncConflictRequest request,
        CancellationToken cancellationToken);

    Task<Guid> CreateAsync(
        Guid tenantId,
        Guid? terminalId,
        Guid? inboxEventId,
        Guid? localEventId,
        string entityType,
        Guid entityId,
        long? localVersion,
        long? serverVersion,
        JsonElement localPayload,
        JsonElement serverPayload,
        CancellationToken cancellationToken);

    Task<SyncBootstrapResponse> ReadBootstrapAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        DateTimeOffset serverTime,
        string initialCursor,
        CancellationToken cancellationToken);
}
