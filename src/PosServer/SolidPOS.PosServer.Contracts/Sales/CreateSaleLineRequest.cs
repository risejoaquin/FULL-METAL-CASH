namespace SolidPOS.PosServer.Contracts.Sales;

public sealed record CreateSaleLineRequest(
    Guid ProductId,
    Guid? VariantId,
    string Quantity,
    long DiscountCents,
    string? PreparationNote,
    IReadOnlyCollection<Guid>? ModifierIds,
    Guid? DiscountId = null);
