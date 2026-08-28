namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record RetrySyncDeadLetterResponse(
    Guid InboxEventId,
    Guid EventId,
    string Status,
    int Attempts,
    DateTimeOffset? NextRetryAt,
    string Message);
