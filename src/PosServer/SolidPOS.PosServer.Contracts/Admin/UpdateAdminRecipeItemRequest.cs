namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminRecipeItemRequest(
    Guid IngredientProductId,
    Guid? IngredientVariantId,
    string Quantity,
    Guid UnitId,
    bool Optional);
