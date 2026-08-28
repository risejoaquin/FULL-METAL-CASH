namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record ReceiptPaymentResponse(
    Guid Id,
    string MethodCode,
    string MethodType,
    long AmountCents,
    string Currency,
    string Status,
    string? Reference,
    DateTimeOffset CreatedAt);
