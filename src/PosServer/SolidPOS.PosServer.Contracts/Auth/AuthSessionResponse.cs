namespace SolidPOS.PosServer.Contracts.Auth;

public sealed record AuthSessionResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    AuthUserResponse User,
    AuthTenantResponse Tenant);
