namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryCountLineResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    Guid UnitId,
    string PreviousQuantity,
    string CountedQuantity,
    string AdjustmentDelta);
