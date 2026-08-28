namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record DashboardSalesTrendPointResponse(
    DateTimeOffset BucketStart,
    long CompletedSalesCount,
    long NetSalesCents,
    long TaxCents,
    long TipCents,
    long TotalSalesCents);
