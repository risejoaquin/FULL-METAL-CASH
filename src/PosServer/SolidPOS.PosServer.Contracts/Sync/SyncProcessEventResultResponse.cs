namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncProcessEventResultResponse(
    Guid InboxEventId,
    Guid EventId,
    string EventType,
    string Status,
    string? ErrorCode,
    string? ErrorMessage);
