using SolidPOS.PosServer.Contracts.Sales;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Sales;

public sealed class SaleContractTests
{
    [Fact]
    public void Sale_contract_can_represent_completed_ticket()
    {
        Guid saleId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid cashShiftId = Guid.NewGuid();
        Guid cashierUserId = Guid.NewGuid();
        Guid localSaleId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid paymentMethodId = Guid.NewGuid();
        Guid localPaymentId = Guid.NewGuid();

        SaleResponse sale = new(
            saleId,
            tenantId,
            storeId,
            terminalId,
            cashShiftId,
            null,
            cashierUserId,
            localSaleId,
            "completed",
            6500,
            0,
            0,
            0,
            6500,
            6500,
            0,
            "MXN",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            DateTimeOffset.UtcNow,
            [new SaleLineResponse(Guid.NewGuid(), productId, null, 1, "Latte 12oz", "1", Guid.NewGuid(), 6500, 0, 0, 6500, Guid.NewGuid(), null, [])],
            [new SalePaymentResponse(Guid.NewGuid(), paymentMethodId, localPaymentId, "cash", "cash", 6500, "MXN", "approved", null, DateTimeOffset.UtcNow)]);

        Assert.Equal(localSaleId, sale.LocalSaleId);
        Assert.Equal(6500, sale.TotalCents);
        Assert.Single(sale.Lines);
        Assert.Single(sale.Payments);
    }


    [Fact]
    public void Sale_detail_contract_can_reconstruct_sale_with_modifiers_and_inventory_movements()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid saleId = Guid.NewGuid();
        Guid modifierId = Guid.NewGuid();

        SaleDetailResponse detail = new(
            saleId,
            tenantId,
            storeId,
            terminalId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "completed",
            7300,
            0,
            0,
            0,
            7300,
            10000,
            2700,
            "MXN",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            DateTimeOffset.UtcNow,
            [new SaleDetailLineResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                1,
                "Latte 12oz",
                "1.0000",
                Guid.NewGuid(),
                7300,
                0,
                0,
                7300,
                Guid.NewGuid(),
                "oat",
                [modifierId],
                [new SaleModifierResponse(modifierId, "Leche de avena", 800, "substitute", Guid.NewGuid(), null, "250", Guid.NewGuid(), Guid.NewGuid(), null)])],
            [],
            [new SaleInventoryMovementResponse(Guid.NewGuid(), storeId, terminalId, Guid.NewGuid(), null, "ING-AVENA-ML", "Leche de avena", "sale_recipe_component", "-250", Guid.NewGuid(), "ml", "modifier_substitute_component", null, modifierId, "substitute", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);

        Assert.Single(detail.Lines);
        Assert.Single(detail.Lines.First().Modifiers);
        Assert.Single(detail.InventoryMovements);
        Assert.Equal("substitute", detail.InventoryMovements.First().ModifierBehavior);
    }

    [Fact]
    public void Void_sale_contract_can_represent_audited_void_request()
    {
        VoidSaleRequest request = new(
            Guid.NewGuid(),
            "Error de captura",
            DateTimeOffset.UtcNow);

        Assert.NotEqual(Guid.Empty, request.VoidedByUserId);
        Assert.Equal("Error de captura", request.Reason);
    }
}
