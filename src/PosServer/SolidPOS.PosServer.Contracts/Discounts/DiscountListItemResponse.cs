namespace SolidPOS.PosServer.Contracts.Discounts;

public sealed record DiscountListItemResponse(
    Guid Id,
    string? Code,
    string Name,
    string DiscountType,
    decimal Value,
    Guid? StoreId,
    Guid? CategoryId,
    Guid? ProductId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string Status,
    DateTimeOffset UpdatedAt);
