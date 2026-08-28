using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogProductResponse(
    Guid Id,
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
    long Version,
    DateTimeOffset UpdatedAt);
