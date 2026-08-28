namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record InventoryPolicyResponse(
    Guid TenantId,
    Guid? StoreId,
    bool AllowNegativeStock,
    bool EnforceAtSale,
    string OfflineSaleBehavior,
    bool LowStockAlertsEnabled,
    DateTimeOffset UpdatedAt);
