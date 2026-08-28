namespace SolidPOS.PosServer.Contracts.Inventory;

public sealed record UpdateInventoryPolicyRequest(
    Guid? StoreId,
    bool AllowNegativeStock,
    bool EnforceAtSale,
    string OfflineSaleBehavior,
    bool LowStockAlertsEnabled);
