namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    string Name,
    string Fingerprint,
    string Status,
    string? AppVersion,
    DateTimeOffset? LastSeenAt);
