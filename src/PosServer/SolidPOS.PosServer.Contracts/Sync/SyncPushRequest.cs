namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncPushRequest(
    Guid BatchId,
    IReadOnlyCollection<SyncPushEventRequest> Events);
