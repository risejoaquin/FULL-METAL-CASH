using System.Text.Json;

namespace SolidPOS.PosServer.Application.Sync;

public sealed record SyncPushEventEnvelope(
    Guid EventId,
    string EventType,
    string EntityType,
    Guid? EntityId,
    DateTimeOffset LocalOccurredAt,
    int SchemaVersion,
    JsonElement Payload,
    int SequenceNumber,
    string PayloadHash);
