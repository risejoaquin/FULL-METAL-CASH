namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record TopProductReportItemResponse(
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    string QuantitySold,
    long LineCount,
    long TotalCents);
