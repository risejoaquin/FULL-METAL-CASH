namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogRecipeResponse(
    Guid Id,
    Guid OutputProductId,
    Guid? OutputVariantId,
    int Version,
    string YieldQuantity,
    Guid YieldUnitId,
    string WastePercent,
    string Status);
