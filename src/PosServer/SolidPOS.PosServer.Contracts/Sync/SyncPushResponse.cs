namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncPushResponse(
    Guid BatchId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    int ReceivedCount,
    int AcceptedCount,
    int DuplicateCount,
    int RejectedCount,
    IReadOnlyCollection<SyncPushEventResultResponse> Results);
