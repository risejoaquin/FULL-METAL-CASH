namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record SalesRangeReportResponse(
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset From,
    DateTimeOffset To,
    long CompletedSalesCount,
    long VoidedSalesCount,
    long ReturnCount,
    long RefundCents,
    long GrossSalesCents,
    long DiscountCents,
    long TaxCents,
    long TipCents,
    long NetSalesCents,
    long TotalSalesCents,
    long NetAfterReturnsCents,
    long AverageTicketCents);
