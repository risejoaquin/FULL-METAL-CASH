namespace SolidPOS.PosServer.Contracts.Auth;

public sealed record LogoutRequest(string RefreshToken, Guid? TenantId = null);
