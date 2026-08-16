namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminProductPriceResponse(
    Guid Id,
    Guid TenantId,
    Guid PriceListId,
    Guid ProductId,
    Guid? VariantId,
    long PriceCents,
    string Currency,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);
