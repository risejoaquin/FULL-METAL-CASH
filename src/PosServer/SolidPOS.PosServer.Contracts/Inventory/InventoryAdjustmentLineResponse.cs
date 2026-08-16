namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryAdjustmentLineResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    string MovementType,
    string QuantityDelta,
    Guid UnitId,
    long? CostCents);
