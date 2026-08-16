namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminRecipeRequest(
    Guid OutputProductId,
    Guid? OutputVariantId,
    int Version,
    string YieldQuantity,
    Guid YieldUnitId,
    string WastePercent,
    string Status);
