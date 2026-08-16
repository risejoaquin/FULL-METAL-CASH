namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncPushEventResultResponse(
    Guid EventId,
    string Status,
    string? Reason);
