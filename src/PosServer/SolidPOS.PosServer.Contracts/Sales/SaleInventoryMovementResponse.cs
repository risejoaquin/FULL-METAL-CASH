namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record SaleInventoryMovementResponse(
    Guid Id,
    Guid StoreId,
    Guid? TerminalId,
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    string MovementType,
    string QuantityDelta,
    Guid UnitId,
    string UnitCode,
    string? Effect,
    Guid? RecipeId,
    Guid? ModifierId,
    string? ModifierBehavior,
    DateTimeOffset LocalOccurredAt,
    DateTimeOffset CreatedAt);
