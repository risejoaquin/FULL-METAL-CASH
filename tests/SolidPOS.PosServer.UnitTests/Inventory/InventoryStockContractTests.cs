using System.Text.Json;
using SolidPOS.PosServer.Contracts.Inventory;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Inventory;

public sealed class InventoryStockContractTests
{
    [Fact]
    public void Inventory_stock_item_can_represent_negative_quantity_from_append_only_ledger()
    {
        InventoryStockItemResponse stock = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "-269.0000");

        string json = JsonSerializer.Serialize(stock);
        InventoryStockItemResponse? deserialized = JsonSerializer.Deserialize<InventoryStockItemResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(stock.TenantId, deserialized.TenantId);
        Assert.Equal("-269.0000", deserialized.QuantityOnHand);
    }
}
