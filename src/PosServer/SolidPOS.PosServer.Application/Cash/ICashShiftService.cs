using SolidPOS.PosServer.Contracts.Cash;

namespace SolidPOS.PosServer.Application.Cash;

public interface ICashShiftService
{
    Task<CashShiftResponse?> GetCurrentOpenShiftAsync(CancellationToken cancellationToken);

    Task<CashShiftResponse?> OpenAsync(OpenCashShiftRequest request, CancellationToken cancellationToken);

    Task<CashMovementResponse?> CreateMovementAsync(Guid shiftId, CreateCashMovementRequest request, CancellationToken cancellationToken);

    Task<CashShiftResponse?> CloseAsync(Guid shiftId, CloseCashShiftRequest request, CancellationToken cancellationToken);
}

