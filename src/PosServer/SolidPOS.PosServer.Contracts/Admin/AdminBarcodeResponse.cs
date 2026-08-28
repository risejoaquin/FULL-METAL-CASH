namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminBarcodeResponse(
    Guid Id,
    Guid TenantId,
    Guid ProductId,
    Guid? VariantId,
    string Barcode,
    string Quantity,
    Guid? UnitId,
    long Version);
