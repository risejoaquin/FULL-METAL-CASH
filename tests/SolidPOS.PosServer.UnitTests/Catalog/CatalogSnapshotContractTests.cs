using System.Text.Json;
using SolidPOS.PosServer.Contracts.Catalog;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Catalog;

public sealed class CatalogSnapshotContractTests
{
    [Fact]
    public void Catalog_snapshot_can_represent_qsr_runtime_payload()
    {
        Guid tenantId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid unitId = Guid.NewGuid();
        Guid productId = Guid.NewGuid();
        Guid variantId = Guid.NewGuid();
        Guid barcodeId = Guid.NewGuid();
        Guid priceListId = Guid.NewGuid();
        Guid modifierGroupId = Guid.NewGuid();
        Guid modifierId = Guid.NewGuid();
        Guid recipeId = Guid.NewGuid();

        CatalogSnapshotResponse snapshot = new(
            tenantId,
            DateTimeOffset.UtcNow,
            [new CatalogCategoryResponse(categoryId, null, "Bebidas", 10, "active", 1, DateTimeOffset.UtcNow)],
            [new CatalogUnitResponse(unitId, "unit", "Unit", "u", "1", true)],
            [new CatalogProductResponse(productId, categoryId, "LATTE-12", "Latte 12oz", null, "recipe_item", unitId, null, true, false, true, "taxable", JsonDocument.Parse("{}").RootElement.Clone(), "active", 1, DateTimeOffset.UtcNow)],
            [new CatalogVariantResponse(variantId, productId, "LATTE-16", "Latte 16oz", JsonDocument.Parse("""{"size":"16oz"}""").RootElement.Clone(), "active", 1, DateTimeOffset.UtcNow)],
            [new CatalogBarcodeResponse(barcodeId, productId, variantId, "7500000000010", "1", unitId)],
            [new CatalogPriceListResponse(priceListId, "DEFAULT", "Default", "MXN", "active")],
            [new CatalogPriceResponse(Guid.NewGuid(), priceListId, productId, variantId, 6500, "MXN", null, null)],
            [new CatalogModifierGroupResponse(modifierGroupId, "Tipo de leche", 1, 1, true)],
            [new CatalogModifierResponse(modifierId, modifierGroupId, "Leche de avena", 800, productId, null, "substitute", "250", unitId, productId, null)],
            [new CatalogProductModifierGroupResponse(productId, modifierGroupId)],
            [new CatalogRecipeResponse(recipeId, productId, null, 1, "1", unitId, "0", "active")],
            []);

        Assert.Equal(tenantId, snapshot.TenantId);
        Assert.Single(snapshot.Categories);
        Assert.Single(snapshot.Products);
        Assert.Single(snapshot.Variants);
        Assert.Single(snapshot.Barcodes);
        Assert.Single(snapshot.Prices);
        Assert.Single(snapshot.ModifierGroups);
        Assert.Single(snapshot.Modifiers);
        Assert.Single(snapshot.ProductModifierGroups);
        Assert.Single(snapshot.Recipes);
    }
}
