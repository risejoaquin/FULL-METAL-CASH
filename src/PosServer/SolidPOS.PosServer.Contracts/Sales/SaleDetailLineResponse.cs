namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record SaleDetailLineResponse(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    int LineNumber,
    string Description,
    string Quantity,
    Guid? UnitId,
    long UnitPriceCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    Guid? RecipeId,
    string? PreparationNote,
    IReadOnlyCollection<Guid> ModifierIds,
    IReadOnlyCollection<SaleModifierResponse> Modifiers);
