namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogCategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    int SortOrder,
    string Status,
    long Version,
    DateTimeOffset UpdatedAt);
