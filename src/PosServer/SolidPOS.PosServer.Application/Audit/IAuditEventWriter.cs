using System.Text.Json;

namespace SolidPOS.PosServer.Application.Audit;

public interface IAuditEventWriter
{
    Task AppendAsync(
        Guid tenantId,
        string action,
        string entityType,
        Guid? entityId,
        JsonElement? beforeData,
        JsonElement? afterData,
        CancellationToken cancellationToken);
}
