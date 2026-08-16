namespace SolidPOS.PosServer.Contracts.Cash;

public sealed record CloseCashShiftRequest(
    Guid ClosedByUserId,
    long CountedCashCents);

