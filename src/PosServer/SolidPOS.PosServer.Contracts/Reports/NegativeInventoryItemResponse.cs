namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record NegativeInventoryItemResponse(
    Guid StoreId,
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    Guid UnitId,
    string UnitCode,
    string QuantityOnHand);
