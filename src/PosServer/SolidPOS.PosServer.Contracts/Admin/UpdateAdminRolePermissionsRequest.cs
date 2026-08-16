namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminRolePermissionsRequest(
    IReadOnlyCollection<string> PermissionCodes);
