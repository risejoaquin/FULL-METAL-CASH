namespace SolidPOS.PosServer.Contracts.Auth;

public sealed record LoginRequest(
    string Email,
    string Password,
    Guid? TenantId = null);
