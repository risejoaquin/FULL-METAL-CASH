namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncProcessRequest(
    Guid? BatchId,
    int MaxEvents);
