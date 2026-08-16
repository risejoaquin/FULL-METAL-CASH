namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminCategoryResponse(
    Guid Id,
    Guid TenantId,
    Guid? ParentId,
    string Name,
    int SortOrder,
    string Status,
    long Version,
    DateTimeOffset UpdatedAt);
