namespace SolidPOS.PosServer.Contracts.Cash;

public sealed record CreateCashMovementRequest(
    string MovementType,
    long AmountCents,
    string Reason,
    Guid CreatedByUserId,
    Guid? AuthorizedByUserId);

