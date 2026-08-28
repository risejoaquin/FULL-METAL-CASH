namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record CreateTerminalEnrollmentTokenRequest(
    Guid StoreId,
    int ExpiresInMinutes = 60);
