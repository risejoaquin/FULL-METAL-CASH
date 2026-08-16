using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncPushEventRequest(
    Guid EventId,
    string EventType,
    string EntityType,
    Guid? EntityId,
    DateTimeOffset LocalOccurredAt,
    int SchemaVersion,
    JsonElement Payload);
