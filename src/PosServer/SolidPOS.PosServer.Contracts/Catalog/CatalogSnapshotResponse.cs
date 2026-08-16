namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogSnapshotResponse(
    Guid TenantId,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<CatalogCategoryResponse> Categories,
    IReadOnlyCollection<CatalogUnitResponse> Units,
    IReadOnlyCollection<CatalogProductResponse> Products,
    IReadOnlyCollection<CatalogVariantResponse> Variants,
    IReadOnlyCollection<CatalogBarcodeResponse> Barcodes,
    IReadOnlyCollection<CatalogPriceListResponse> PriceLists,
    IReadOnlyCollection<CatalogPriceResponse> Prices,
    IReadOnlyCollection<CatalogModifierGroupResponse> ModifierGroups,
    IReadOnlyCollection<CatalogModifierResponse> Modifiers,
    IReadOnlyCollection<CatalogProductModifierGroupResponse> ProductModifierGroups,
    IReadOnlyCollection<CatalogRecipeResponse> Recipes,
    IReadOnlyCollection<CatalogRecipeItemResponse> RecipeItems);
