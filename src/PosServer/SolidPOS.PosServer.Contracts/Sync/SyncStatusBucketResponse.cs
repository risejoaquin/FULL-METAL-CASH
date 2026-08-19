namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncStatusBucketResponse(
    string Status,
    int Count);
