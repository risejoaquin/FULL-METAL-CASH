namespace SolidPOS.PosServer.Contracts.Auth;

public sealed record RefreshTokenRequest(string RefreshToken, Guid? TenantId = null);
