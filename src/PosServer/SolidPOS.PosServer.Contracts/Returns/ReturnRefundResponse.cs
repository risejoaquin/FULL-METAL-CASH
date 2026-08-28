namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record ReturnRefundResponse(
    Guid Id,
    string MethodCode,
    string MethodType,
    long AmountCents,
    string Currency,
    string Status,
    string? Reference,
    DateTimeOffset CreatedAt);
