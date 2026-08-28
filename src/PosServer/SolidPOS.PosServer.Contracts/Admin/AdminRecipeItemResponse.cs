namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminRecipeItemResponse(
    Guid Id,
    Guid TenantId,
    Guid RecipeId,
    Guid IngredientProductId,
    Guid? IngredientVariantId,
    string Quantity,
    Guid UnitId,
    bool Optional,
    long Version);
