namespace SolidPOS.PosServer.Application.Auth;

public sealed record AuthenticatedTerminal(
    Guid TerminalId,
    Guid TenantId,
    Guid StoreId,
    string Name,
    string Status);
