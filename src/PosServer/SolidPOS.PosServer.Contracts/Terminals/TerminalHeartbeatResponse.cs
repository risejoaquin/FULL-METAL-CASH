namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalHeartbeatResponse(
    Guid TerminalId,
    Guid TenantId,
    Guid StoreId,
    string Status,
    DateTimeOffset LastSeenAt,
    TerminalRemoteConfigMetadata RemoteConfig);
