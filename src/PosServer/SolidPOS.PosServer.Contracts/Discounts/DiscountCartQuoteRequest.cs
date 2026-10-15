namespace SolidPOS.PosServer.Contracts.Discounts;

/// <summary>
/// A PREVIEW, not a server-authorized settlement instruction.
/// Client-supplied gross prices are never trusted for CreateSale.
/// </summary>
public sealed record DiscountCartQuoteLineRequest(
    Guid LineId, Guid ProductId, long GrossCents, Guid? LineDiscountId = null);

public sealed record DiscountCartQuoteRequest(
    Guid StoreId, IReadOnlyList<DiscountCartQuoteLineRequest> Lines,
    Guid? CartDiscountId = null, DateTimeOffset? OccurredAt = null);
