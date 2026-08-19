namespace SolidPOS.PosCore.Domain;

public sealed record LocalInventoryRecipe(
    Guid RecipeId,
    Guid OutputProductId,
    Guid? OutputVariantId,
    decimal YieldQuantity,
    Guid YieldUnitId,
    decimal WastePercent,
    string Status,
    DateTimeOffset SyncedAtUtc);

public sealed record LocalInventoryRecipeItem(
    Guid RecipeItemId,
    Guid RecipeId,
    Guid IngredientProductId,
    Guid? IngredientVariantId,
    decimal Quantity,
    Guid UnitId,
    bool Optional,
    DateTimeOffset SyncedAtUtc);

public sealed record LocalInventoryMovement(
    Guid Id,
    Guid LocalSaleId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid ProductId,
    Guid? VariantId,
    string MovementType,
    decimal QuantityDelta,
    Guid UnitId,
    DateTimeOffset OccurredAtUtc,
    string Source,
    DateTimeOffset CreatedAtUtc);

public sealed record LocalInventoryConsumptionPreview(
    Guid LocalSaleId,
    int MovementCount,
    decimal TotalQuantityDelta);
