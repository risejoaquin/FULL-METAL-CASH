namespace SolidPOS.PosServer.Contracts.Cash;

public sealed record CashShiftResponse(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid OpenedByUserId,
    Guid? ClosedByUserId,
    string Status,
    long OpeningAmountCents,
    long ExpectedCashCents,
    long? CountedCashCents,
    long? DifferenceCents,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt);

