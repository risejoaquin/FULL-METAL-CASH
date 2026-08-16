namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record CustomerListItemResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Email,
    string? Phone,
    long CreditLimitCents,
    string Status,
    long SalesCount,
    long GrossSalesCents,
    long RefundCents,
    long NetSpentCents,
    long AverageTicketCents,
    DateTimeOffset? LastPurchaseAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
