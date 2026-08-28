namespace SolidPOS.PosServer.Contracts.Auth;

public sealed record AuthTenantResponse(
    Guid Id,
    string Name,
    string Status);
