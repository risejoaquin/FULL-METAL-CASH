namespace SolidPOS.PosServer.Application.Discounts;

public sealed record DiscountListFilters(
    string? Search,
    string? Status,
    Guid? StoreId,
    Guid? CategoryId,
    Guid? ProductId,
    int Limit);
