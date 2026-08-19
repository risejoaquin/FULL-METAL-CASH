namespace SolidPOS.PosCore.Domain;

public enum LocalOutboxStatus
{
    Pending = 0,
    InFlight = 1,
    Synced = 2,
    Failed = 3,
    DeadLetter = 4
}

public sealed record LocalOutboxEvent(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    string EventType,
    int SchemaVersion,
    long SequenceNumber,
    string PayloadJson,
    LocalOutboxStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SyncedAtUtc = null,
    string? LastError = null,
    int Attempts = 0);

public sealed record LocalOutboxBatch(
    Guid BatchId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    IReadOnlyList<LocalOutboxEvent> Events);
