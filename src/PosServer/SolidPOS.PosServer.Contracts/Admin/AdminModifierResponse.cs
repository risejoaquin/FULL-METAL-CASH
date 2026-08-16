namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminModifierResponse(
    Guid Id,
    Guid TenantId,
    Guid GroupId,
    string Name,
    long PriceDeltaCents,
    Guid? LinkedProductId,
    Guid? LinkedVariantId,
    string InventoryBehavior,
    string? ConsumptionQuantity,
    Guid? ConsumptionUnitId,
    Guid? ReplacesProductId,
    Guid? ReplacesVariantId,
    long Version,
    DateTimeOffset UpdatedAt);
