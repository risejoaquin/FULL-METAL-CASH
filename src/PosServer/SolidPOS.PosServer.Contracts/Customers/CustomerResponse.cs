namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record CustomerResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Email,
    string? Phone,
    long CreditLimitCents,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
