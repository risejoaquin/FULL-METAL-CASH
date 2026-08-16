namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record CustomerSalesSummaryResponse(
    long SalesCount,
    long GrossSalesCents,
    long RefundCents,
    long NetSpentCents,
    long AverageTicketCents,
    DateTimeOffset? LastPurchaseAt);
