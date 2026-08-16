namespace SolidPOS.PosServer.Contracts.Cash;

public sealed record OpenCashShiftRequest(
    Guid? StoreId,
    Guid? TerminalId,
    Guid OpenedByUserId,
    long OpeningAmountCents);

