namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record CreateInventoryCountLineRequest(
    Guid ProductId,
    Guid? VariantId,
    Guid UnitId,
    string CountedQuantity);
