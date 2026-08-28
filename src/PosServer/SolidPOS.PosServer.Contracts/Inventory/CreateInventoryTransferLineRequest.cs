namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record CreateInventoryTransferLineRequest(
    Guid ProductId,
    Guid? VariantId,
    Guid UnitId,
    string Quantity);
