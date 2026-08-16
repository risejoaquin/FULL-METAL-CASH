namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record SalePaymentResponse(
    Guid Id,
    Guid PaymentMethodId,
    Guid LocalPaymentId,
    string MethodCode,
    string MethodType,
    long AmountCents,
    string Currency,
    string Status,
    string? Reference,
    DateTimeOffset CreatedAt);

