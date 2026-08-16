using System.Text.Json;
using SolidPOS.PosServer.Contracts.Sync;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncPullRepository
{
    Task<IReadOnlyCollection<SyncPullChangeResponse>> ReadChangesAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        DateTimeOffset? changedAfter,
        int limit,
        CancellationToken cancellationToken);

    Task<JsonElement> ReadAccessSnapshotAsync(Guid tenantId, CancellationToken cancellationToken);
}
