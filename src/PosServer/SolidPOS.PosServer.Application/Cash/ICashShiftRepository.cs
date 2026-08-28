using SolidPOS.PosServer.Contracts.Cash;

namespace SolidPOS.PosServer.Application.Cash;

public interface ICashShiftRepository
{
    Task<CashShiftResponse?> GetCurrentOpenShiftAsync(Guid tenantId, Guid storeId, Guid terminalId, CancellationToken cancellationToken);

    Task<CashShiftOperationalSummaryResponse?> GetOperationalSummaryAsync(Guid tenantId, Guid shiftId, CancellationToken cancellationToken);

    Task<CashShiftResponse?> OpenAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid openedByUserId,
        long openingAmountCents,
        CancellationToken cancellationToken);

    Task<CashMovementResponse?> CreateMovementAsync(
        Guid tenantId,
        Guid shiftId,
        string movementType,
        long amountCents,
        string reason,
        Guid createdByUserId,
        Guid? authorizedByUserId,
        CancellationToken cancellationToken);

    Task<CashShiftResponse?> CloseAsync(
        Guid tenantId,
        Guid shiftId,
        Guid closedByUserId,
        long countedCashCents,
        CancellationToken cancellationToken);
}

