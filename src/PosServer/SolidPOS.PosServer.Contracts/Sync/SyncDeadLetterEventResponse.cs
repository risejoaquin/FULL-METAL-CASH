using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncDeadLetterEventResponse(
    Guid InboxEventId,
    Guid EventId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid? BatchId,
    string EventType,
    string EntityType,
    Guid? EntityId,
    int SchemaVersion,
    int Attempts,
    int MaxAttempts,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset LocalOccurredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? DeadLetteredAt,
    JsonElement Payload);
