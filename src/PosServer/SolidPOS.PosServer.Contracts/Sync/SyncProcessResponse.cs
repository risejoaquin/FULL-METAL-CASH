namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncProcessResponse(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid? BatchId,
    int ReceivedCount,
    int ProcessedCount,
    int RejectedCount,
    IReadOnlyCollection<SyncProcessEventResultResponse> Results);
