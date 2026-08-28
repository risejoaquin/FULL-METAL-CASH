using System.Text.Json;
using SolidPOS.PosServer.Contracts.Inventory;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Inventory;

public sealed class InventoryAdjustmentContractTests
{
    [Fact]
    public void Inventory_adjustment_contract_can_represent_manual_stock_in()
    {
        Guid localAdjustmentId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid unitId = Guid.NewGuid();

        CreateInventoryAdjustmentRequest request = new(
            localAdjustmentId,
            Guid.NewGuid(),
            "stock_in",
            "Conteo fisico",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [new CreateInventoryAdjustmentLineRequest(productId, null, "18.0000", unitId, null)]);

        string json = JsonSerializer.Serialize(request);
        CreateInventoryAdjustmentRequest? deserialized = JsonSerializer.Deserialize<CreateInventoryAdjustmentRequest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(localAdjustmentId, deserialized.LocalAdjustmentId);
        Assert.Single(deserialized.Lines);
    }
}
