namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record UpdateAdminCategoryRequest(
    Guid? ParentId,
    string Name,
    int SortOrder,
    string Status,
    long? ExpectedVersion = null);
