namespace SolidPOS.PosCore.Domain;

public sealed record OfflineSaleLineDraft(
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    int Quantity,
    int UnitPriceCents,
    int DiscountCents = 0,
    Guid? DiscountId = null,
    string? PreparationNote = null,
    IReadOnlyCollection<Guid>? ModifierIds = null)
{
    public int GrossCents => Quantity * UnitPriceCents;
    public int NetCents => GrossCents - DiscountCents;
}

public sealed record OfflineSalePaymentDraft(
    string MethodCode,
    int AmountCents,
    Guid? LocalPaymentId = null,
    string? Reference = null);

public sealed record OfflineSaleDraft(
    Guid LocalSaleId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<OfflineSaleLineDraft> Lines,
    IReadOnlyList<OfflineSalePaymentDraft> Payments,
    string Currency,
    Guid? CashierUserId = null,
    Guid? CustomerId = null,
    long TipCents = 0)
{
    public int SubtotalCents => Lines.Sum(line => line.GrossCents);
    public int DiscountCents => Lines.Sum(line => line.DiscountCents);
    public int TotalCents => Lines.Sum(line => line.NetCents) + (int)TipCents;
    public int PaidCents => Payments.Sum(payment => payment.AmountCents);
}
