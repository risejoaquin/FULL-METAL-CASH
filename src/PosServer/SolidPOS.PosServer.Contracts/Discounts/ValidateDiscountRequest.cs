namespace SolidPOS.PosServer.Contracts.Discounts;

public sealed record ValidateDiscountRequest(
    Guid DiscountId,
    Guid? StoreId,
    Guid ProductId,
    Guid? CategoryId,
    string Quantity,
    long UnitPriceCents,
    DateTimeOffset? OccurredAt);
