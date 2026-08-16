namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record UpdateCustomerRequest(
    string? Name,
    string? Email,
    string? Phone,
    long? CreditLimitCents,
    string? Status);
