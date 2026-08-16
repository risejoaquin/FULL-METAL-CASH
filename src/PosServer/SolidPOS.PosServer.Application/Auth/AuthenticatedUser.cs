namespace SolidPOS.PosServer.Application.Auth;

public sealed record AuthenticatedUser(
    Guid UserId,
    Guid TenantId,
    string Email,
    string Name,
    string TenantName,
    string TenantStatus,
    string UserStatus,
    string PasswordHash);
