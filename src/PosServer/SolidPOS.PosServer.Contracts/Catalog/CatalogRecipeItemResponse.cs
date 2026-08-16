namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogRecipeItemResponse(
    Guid Id,
    Guid RecipeId,
    Guid IngredientProductId,
    Guid? IngredientVariantId,
    string Quantity,
    Guid UnitId,
    bool Optional);
