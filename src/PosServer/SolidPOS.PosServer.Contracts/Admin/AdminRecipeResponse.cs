namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminRecipeResponse(
    Guid Id,
    Guid TenantId,
    Guid OutputProductId,
    Guid? OutputVariantId,
    int Version,
    string YieldQuantity,
    Guid YieldUnitId,
    string WastePercent,
    string Status,
    DateTimeOffset UpdatedAt);
