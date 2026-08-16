namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminBarcodeRequest(
    Guid ProductId,
    Guid? VariantId,
    string Barcode,
    string Quantity,
    Guid? UnitId);
