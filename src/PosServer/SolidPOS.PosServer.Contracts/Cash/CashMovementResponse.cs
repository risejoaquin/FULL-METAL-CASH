namespace SolidPOS.PosServer.Contracts.Cash;

public sealed record CashMovementResponse(
    Guid Id,
    Guid TenantId,
    Guid CashShiftId,
    string MovementType,
    long AmountCents,
    string Reason,
    Guid? AuthorizedByUserId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt);

