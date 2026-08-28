namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryStockItemResponse(
    Guid TenantId,
    Guid StoreId,
    Guid ProductId,
    Guid? VariantId,
    Guid UnitId,
    string QuantityOnHand);

