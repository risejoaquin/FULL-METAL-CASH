namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record CreateInventoryAdjustmentLineRequest(
    Guid ProductId,
    Guid? VariantId,
    string QuantityDelta,
    Guid UnitId,
    long? CostCents);
