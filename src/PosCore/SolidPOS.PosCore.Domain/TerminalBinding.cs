namespace SolidPOS.PosCore.Domain;

public sealed record TerminalBinding(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    string TerminalFingerprint,
    string TerminalToken,
    DateTimeOffset BoundAtUtc,
    int SchemaVersion);
