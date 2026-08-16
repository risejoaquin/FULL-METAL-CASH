namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record SaleListItemResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid? CashShiftId,
    Guid? CustomerId,
    Guid CashierUserId,
    Guid LocalSaleId,
    string Status,
    long TotalCents,
    long PaidCents,
    long ChangeCents,
    string Currency,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    int LineCount,
    int PaymentCount);
