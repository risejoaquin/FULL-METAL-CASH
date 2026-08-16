namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogBarcodeResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    string Barcode,
    string Quantity,
    Guid? UnitId);
