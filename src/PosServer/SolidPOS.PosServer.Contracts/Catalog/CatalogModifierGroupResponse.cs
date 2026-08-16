namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogModifierGroupResponse(
    Guid Id,
    string Name,
    int MinSelected,
    int MaxSelected,
    bool Required);
