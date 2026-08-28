namespace SolidPOS.PosCore.Domain;

public sealed record LocalCashShift(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid OpenedByUserId,
    DateTimeOffset OpenedAtUtc,
    int OpeningAmountCents,
    string Status,
    Guid? ClosedByUserId = null,
    DateTimeOffset? ClosedAtUtc = null,
    int? CountedCashCents = null,
    int? ExpectedCashCents = null,
    int? DifferenceCents = null);

public sealed record LocalCashMovement(
    Guid Id,
    Guid ShiftId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    string MovementType,
    int AmountCents,
    DateTimeOffset OccurredAtUtc,
    string SourceType,
    Guid? SourceId = null,
    string? Note = null);

public sealed record LocalSalePaymentSnapshot(
    Guid LocalPaymentId,
    Guid LocalSaleId,
    Guid ShiftId,
    string MethodCode,
    int AmountCents,
    int TenderedCents,
    int ChangeCents,
    DateTimeOffset CreatedAtUtc);

public sealed record LocalCashShiftSummary(
    Guid ShiftId,
    string Status,
    int OpeningAmountCents,
    int CashSalesCents,
    int CashInCents,
    int CashOutCents,
    int ExpectedCashCents,
    int? CountedCashCents,
    int? DifferenceCents,
    int PaymentCount,
    int MovementCount);
