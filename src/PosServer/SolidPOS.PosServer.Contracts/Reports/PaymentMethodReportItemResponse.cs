namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record PaymentMethodReportItemResponse(
    string MethodCode,
    string MethodName,
    string MethodType,
    long PaymentCount,
    long SaleCount,
    long TotalCents);
