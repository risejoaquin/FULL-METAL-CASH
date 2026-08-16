namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminRolePermissionsResponse(
    Guid TenantId,
    Guid RoleId,
    IReadOnlyCollection<string> PermissionCodes,
    long Version,
    DateTimeOffset UpdatedAt);
