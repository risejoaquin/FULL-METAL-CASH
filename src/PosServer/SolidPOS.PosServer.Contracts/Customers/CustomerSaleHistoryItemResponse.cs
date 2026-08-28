namespace SolidPOS.PosServer.Contracts.Customers;

public sealed record CustomerSaleHistoryItemResponse(
    Guid SaleId,
    Guid StoreId,
    Guid TerminalId,
    Guid? CashShiftId,
    string Status,
    long TotalCents,
    long PaidCents,
    long ChangeCents,
    long RefundCents,
    long NetAfterReturnsCents,
    string Currency,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);
