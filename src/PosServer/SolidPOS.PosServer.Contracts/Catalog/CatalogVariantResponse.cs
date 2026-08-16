using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogVariantResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    JsonElement Attributes,
    string Status,
    long Version,
    DateTimeOffset UpdatedAt);
