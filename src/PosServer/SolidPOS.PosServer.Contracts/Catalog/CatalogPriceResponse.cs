namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogPriceResponse(
    Guid Id,
    Guid PriceListId,
    Guid ProductId,
    Guid? VariantId,
    long PriceCents,
    string Currency,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);
