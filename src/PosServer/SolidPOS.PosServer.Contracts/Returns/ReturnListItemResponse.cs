namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record ReturnListItemResponse(
    Guid Id,
    Guid SaleId,
    Guid StoreId,
    Guid TerminalId,
    Guid CashShiftId,
    Guid LocalReturnId,
    string Status,
    string Reason,
    long TotalCents,
    long RefundCents,
    int LineCount,
    int RefundCount,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);
