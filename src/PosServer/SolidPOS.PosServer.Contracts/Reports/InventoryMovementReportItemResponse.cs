namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record InventoryMovementReportItemResponse(
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
    string? ReferenceType,
    Guid? ReferenceId,
    string? Effect,
    Guid? RecipeId,
    Guid? ModifierId,
    string? ModifierBehavior,
    DateTimeOffset LocalOccurredAt,
    DateTimeOffset CreatedAt);
