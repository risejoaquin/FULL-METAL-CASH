namespace SolidPOS.PosServer.Contracts.Auth;

public sealed record AuthUserResponse(
    Guid Id,
    string Email,
    string Name,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
