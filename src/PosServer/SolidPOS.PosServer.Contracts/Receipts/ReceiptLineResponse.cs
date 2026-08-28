namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record ReceiptLineResponse(
    Guid Id,
    int LineNumber,
    string Description,
    string Quantity,
    long UnitPriceCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    string? PreparationNote,
    IReadOnlyCollection<ReceiptModifierResponse> Modifiers);
