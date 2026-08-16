using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminProductRequest(
    Guid? CategoryId,
    string Sku,
    string Name,
    string? Description,
    string ProductType,
    Guid? SaleUnitId,
    Guid? InventoryUnitId,
    bool IsSellable,
    bool IsStockTracked,
    bool AllowNegativeStock,
    string TaxMode,
    JsonElement Attributes,
    string Status,
    long? ExpectedVersion = null);
