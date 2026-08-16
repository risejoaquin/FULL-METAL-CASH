namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminProductPriceRequest(
    Guid PriceListId,
    Guid ProductId,
    Guid? VariantId,
    long PriceCents,
    string Currency = "MXN",
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null);
