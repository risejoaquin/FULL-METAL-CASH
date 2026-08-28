using System.Text.Json;

namespace SolidPOS.PosServer.Application.Sync;

public sealed record SyncInboxEvent(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid? BatchId,
    Guid EventId,
    string EventType,
    string EntityType,
    Guid? EntityId,
    DateTimeOffset LocalOccurredAt,
    int SchemaVersion,
    JsonElement Payload);
