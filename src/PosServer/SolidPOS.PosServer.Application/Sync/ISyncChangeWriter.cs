using System.Text.Json;

namespace SolidPOS.PosServer.Application.Sync;

public interface ISyncChangeWriter
{
    Task AppendAsync(
        Guid tenantId,
        Guid? storeId,
        string entityType,
        Guid entityId,
        string operation,
        long entityVersion,
        JsonElement payload,
        Guid? sourceTerminalId,
        CancellationToken cancellationToken);
}
