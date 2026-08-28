namespace SolidPOS.PosCore.Domain;

public sealed record LocalSyncAcknowledgement(
    Guid Id,
    Guid BatchId,
    Guid OutboxEventId,
    string RemoteStatus,
    string RemoteResponseJson,
    DateTimeOffset AcknowledgedAtUtc);
