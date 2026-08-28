namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogUnitResponse(
    Guid Id,
    string Code,
    string Name,
    string Symbol,
    string FactorToBase,
    bool IsBase);
