namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalEnrollmentTokenResponse(
    Guid TenantId,
    Guid StoreId,
    string EnrollmentToken,
    DateTimeOffset ExpiresAt);
