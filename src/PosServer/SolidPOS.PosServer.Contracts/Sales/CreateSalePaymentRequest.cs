namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record CreateSalePaymentRequest(
    Guid LocalPaymentId,
    string MethodCode,
    long AmountCents,
    string? Reference);

