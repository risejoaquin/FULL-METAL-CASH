using SolidPOS.PosServer.Contracts.Returns;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sales;

public sealed class ReturnContractTests
{
    [Fact]
    public void CreateReturnRequest_supports_partial_return_with_refund()
    {
        Guid saleId = Guid.NewGuid();
        Guid saleLineId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        var request = new CreateReturnRequest(
            Guid.NewGuid(),
            saleId,
            userId,
            "Customer changed order",
            DateTimeOffset.UtcNow,
            [new CreateReturnLineRequest(saleLineId, "0.5")],
            [new CreateReturnRefundRequest("cash", 3650, "partial-refund")]);

        Assert.Equal(saleId, request.SaleId);
        Assert.Equal(saleLineId, request.Lines.Single().SaleLineId);
        Assert.Equal("0.5", request.Lines.Single().Quantity);
        Assert.Equal(3650, request.Refunds.Single().AmountCents);
    }

    [Fact]
    public void ReturnResponse_exposes_refunds_and_inventory_movements()
    {
        var response = new ReturnResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "completed",
            "Return test",
            7300,
            0,
            7300,
            7300,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new ReturnLineResponse(Guid.NewGuid(), Guid.NewGuid(), 1, "Latte 12oz", "1", 7300)],
            [new ReturnRefundResponse(Guid.NewGuid(), "cash", "cash", 7300, "MXN", "approved", "test", DateTimeOffset.UtcNow)],
            [new ReturnInventoryMovementResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "ING-AVENA-ML", "Leche de avena", "return", "250", Guid.NewGuid(), "ml", "modifier_substitute_component", Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        Assert.Single(response.Lines);
        Assert.Single(response.Refunds);
        Assert.Single(response.InventoryMovements);
        Assert.Equal("completed", response.Status);
    }
}
