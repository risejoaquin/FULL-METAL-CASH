namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminUserAccessResponse(
    Guid Id,
    Guid TenantId,
    string Email,
    string FullName,
    string Status,
    IReadOnlyCollection<string> RoleCodes,
    long Version,
    DateTimeOffset UpdatedAt);
