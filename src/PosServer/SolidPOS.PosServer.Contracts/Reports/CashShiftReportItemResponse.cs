namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record CashShiftReportItemResponse(
    Guid Id,
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
