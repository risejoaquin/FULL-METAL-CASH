namespace SolidPOS.PosServer.Contracts.Cash;

public sealed record CashShiftOperationalSummaryResponse(
    Guid ShiftId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    string Status,
    long OpeningAmountCents,
    long ExpectedCashCents,
    long? CountedCashCents,
    long? DifferenceCents,
    long CashSalesCents,
    long NonCashSalesCents,
    long CashRefundsCents,
    long NonCashRefundsCents,
    long CashInCents,
    long CashOutCents,
    int NoSaleDrawerOpenCount,
    int SalesCount,
    int ReturnsCount,
    int MovementCount,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt);
