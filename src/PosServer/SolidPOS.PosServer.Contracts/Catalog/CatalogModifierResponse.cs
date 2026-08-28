namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogModifierResponse(
    Guid Id,
    Guid GroupId,
    string Name,
    long PriceDeltaCents,
    Guid? LinkedProductId,
    Guid? LinkedVariantId,
    string InventoryBehavior,
    string? ConsumptionQuantity,
    Guid? ConsumptionUnitId,
    Guid? ReplacesProductId,
    Guid? ReplacesVariantId);
