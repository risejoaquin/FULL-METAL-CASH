namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryTransferLineResponse(
    Guid ProductId,
    Guid? VariantId,
    Guid UnitId,
    string Quantity);
