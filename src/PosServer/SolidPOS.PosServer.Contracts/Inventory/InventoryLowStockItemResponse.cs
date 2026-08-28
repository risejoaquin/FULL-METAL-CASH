namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryLowStockItemResponse(
    Guid TenantId,
    Guid StoreId,
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    Guid UnitId,
    string QuantityOnHand,
    string ReorderPoint,
    string ReorderQuantity,
    string Severity);
