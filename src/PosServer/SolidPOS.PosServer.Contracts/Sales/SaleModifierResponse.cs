namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record SaleModifierResponse(
    Guid Id,
    string Name,
    long PriceDeltaCents,
    string InventoryBehavior,
    Guid? LinkedProductId,
    Guid? LinkedVariantId,
    string? ConsumptionQuantity,
    Guid? ConsumptionUnitId,
    Guid? ReplacesProductId,
    Guid? ReplacesVariantId);
