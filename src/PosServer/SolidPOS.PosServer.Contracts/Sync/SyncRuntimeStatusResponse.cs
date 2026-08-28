namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncRuntimeStatusResponse(
    Guid TenantId,
    Guid? StoreId,
    Guid? TerminalId,
    DateTimeOffset ServerTime,
    int TotalInboxEvents,
    int PendingCount,
    int ProcessingCount,
    int ProcessedCount,
    int DuplicateCount,
    int RejectedCount,
    int RetryPendingCount,
    int ConflictCount,
    int DeadLetterCount,
    DateTimeOffset? OldestPendingAt,
    DateTimeOffset? LastProcessedAt,
    IReadOnlyCollection<SyncStatusBucketResponse> Buckets);
