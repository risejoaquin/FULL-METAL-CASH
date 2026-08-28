namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminModifierRequest(
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
