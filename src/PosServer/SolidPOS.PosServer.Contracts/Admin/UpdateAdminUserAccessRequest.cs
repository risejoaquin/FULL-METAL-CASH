namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminUserAccessRequest(
    string FullName,
    string Status,
    IReadOnlyCollection<string> RoleCodes);
