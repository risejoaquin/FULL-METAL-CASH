namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record SaleResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid CashShiftId,
    Guid? CustomerId,
    Guid CashierUserId,
    Guid LocalSaleId,
    string Status,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TipCents,
    long TotalCents,
    long PaidCents,
    long ChangeCents,
    string Currency,
    DateTimeOffset OccurredAt,
    DateTimeOffset LocalCreatedAt,
    long Version,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<SaleLineResponse> Lines,
    IReadOnlyCollection<SalePaymentResponse> Payments);

