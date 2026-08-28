using SolidPOS.PosServer.Contracts.Cash;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Cash;

public sealed class CashShiftContractTests
{
    [Fact]
    public void Cash_shift_contract_can_represent_open_movement_and_close_flow()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid openedByUserId = Guid.NewGuid();
        Guid shiftId = Guid.NewGuid();

        OpenCashShiftRequest openRequest = new(storeId, terminalId, openedByUserId, 100000);
        CashShiftResponse openShift = new(
            shiftId,
            tenantId,
            storeId,
            terminalId,
            openedByUserId,
            null,
            "open",
            openRequest.OpeningAmountCents,
            openRequest.OpeningAmountCents,
            null,
            null,
            DateTimeOffset.UtcNow,
            null);

        CreateCashMovementRequest movementRequest = new("cash_out", 15000, "Pago proveedor hielo", openedByUserId, null);
        CashMovementResponse movement = new(
            Guid.NewGuid(),
            tenantId,
            shiftId,
            movementRequest.MovementType,
            movementRequest.AmountCents,
            movementRequest.Reason,
            movementRequest.AuthorizedByUserId,
            movementRequest.CreatedByUserId,
            DateTimeOffset.UtcNow);

        CloseCashShiftRequest closeRequest = new(openedByUserId, 85000);

        Assert.Equal(100000, openShift.ExpectedCashCents);
        Assert.Equal("cash_out", movement.MovementType);
        Assert.Equal(85000, closeRequest.CountedCashCents);
    }
}

