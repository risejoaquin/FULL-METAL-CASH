namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogPriceListResponse(
    Guid Id,
    string Code,
    string Name,
    string Currency,
    string Status);
