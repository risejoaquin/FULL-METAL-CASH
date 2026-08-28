namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record ReturnResponse(
    Guid Id,
    Guid TenantId,
    Guid SaleId,
    Guid StoreId,
    Guid TerminalId,
    Guid CashShiftId,
    Guid LocalReturnId,
    string Status,
    string Reason,
    long SubtotalCents,
    long TaxCents,
    long TotalCents,
    long RefundCents,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<ReturnLineResponse> Lines,
    IReadOnlyCollection<ReturnRefundResponse> Refunds,
    IReadOnlyCollection<ReturnInventoryMovementResponse> InventoryMovements);
