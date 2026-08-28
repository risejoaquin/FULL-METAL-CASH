namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record CreateCustomerRequest(
    string Name,
    string? Email,
    string? Phone,
    long CreditLimitCents);
