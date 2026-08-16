namespace SolidPOS.PosServer.Contracts.Catalog;

public sealed record CatalogProductModifierGroupResponse(
    Guid ProductId,
    Guid ModifierGroupId);
