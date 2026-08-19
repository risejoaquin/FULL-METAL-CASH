using System.Text.Json;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Sync;

public sealed record RemoteSyncPushRequest(Guid BatchId, IReadOnlyList<RemoteSyncPushEvent> Events);

public sealed record RemoteSyncPushEvent(
    Guid EventId,
    string EventType,
    string EntityType,
    Guid EntityId,
    DateTimeOffset LocalOccurredAt,
    int SchemaVersion,
    JsonElement Payload);

public sealed record RemoteSyncPushResult(
    Guid BatchId,
    int AttemptedCount,
    int AcceptedCount,
    int DuplicateCount,
    int FailedCount,
    string RawResponseJson,
    IReadOnlyList<Guid> AcknowledgedEventIds);

public static class RemoteSyncPushMapper
{
    public static RemoteSyncPushRequest Map(LocalOutboxBatch batch)
    {
        var events = batch.Events.Select(MapEvent).ToArray();
        return new RemoteSyncPushRequest(batch.BatchId, events);
    }

    private static RemoteSyncPushEvent MapEvent(LocalOutboxEvent localEvent)
    {
        using var document = JsonDocument.Parse(localEvent.PayloadJson);
        var payload = document.RootElement.Clone();
        var entityType = ResolveEntityType(localEvent.EventType);
        var entityId = ResolveEntityId(entityType, payload, localEvent);

        return new RemoteSyncPushEvent(
            localEvent.Id,
            localEvent.EventType,
            entityType,
            entityId,
            localEvent.CreatedAtUtc,
            localEvent.SchemaVersion,
            payload);
    }

    private static string ResolveEntityType(string eventType)
    {
        if (eventType.StartsWith("sale.", StringComparison.OrdinalIgnoreCase)) return "sale";
        if (eventType.StartsWith("inventory.", StringComparison.OrdinalIgnoreCase)) return "inventory";
        if (eventType.StartsWith("pos.", StringComparison.OrdinalIgnoreCase)) return "terminal";
        return "unknown";
    }

    private static Guid ResolveEntityId(string entityType, JsonElement payload, LocalOutboxEvent localEvent)
    {
        if (entityType == "sale" && payload.TryGetProperty("saleId", out var saleIdProperty) && saleIdProperty.ValueKind == JsonValueKind.String)
        {
            return Guid.Parse(saleIdProperty.GetString()!);
        }

        if (payload.TryGetProperty("entityId", out var entityIdProperty) && entityIdProperty.ValueKind == JsonValueKind.String)
        {
            return Guid.Parse(entityIdProperty.GetString()!);
        }

        return localEvent.TerminalId;
    }
}
