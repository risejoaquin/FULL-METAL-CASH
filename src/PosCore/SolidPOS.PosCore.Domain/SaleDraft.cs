namespace SolidPOS.PosCore.Domain;

public sealed record OfflineSaleLineDraft(
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    int Quantity,
    int UnitPriceCents,
    int DiscountCents = 0)
{
    public int GrossCents => Quantity * UnitPriceCents;
    public int NetCents => GrossCents - DiscountCents;
}

public sealed record OfflineSalePaymentDraft(
    string MethodCode,
    int AmountCents);

public sealed record OfflineSaleDraft(
    Guid LocalSaleId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<OfflineSaleLineDraft> Lines,
    IReadOnlyList<OfflineSalePaymentDraft> Payments,
    string Currency)
{
    public int SubtotalCents => Lines.Sum(line => line.GrossCents);
    public int DiscountCents => Lines.Sum(line => line.DiscountCents);
    public int TotalCents => Lines.Sum(line => line.NetCents);
    public int PaidCents => Payments.Sum(payment => payment.AmountCents);
}
