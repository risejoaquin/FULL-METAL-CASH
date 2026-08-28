namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record CreateReturnRefundRequest(
    string MethodCode,
    long AmountCents,
    string? Reference);
