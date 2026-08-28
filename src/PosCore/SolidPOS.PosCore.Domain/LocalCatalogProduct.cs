namespace SolidPOS.PosCore.Domain;

public sealed record LocalCatalogProduct(
    Guid ProductId,
    Guid? VariantId,
    string Sku,
    string Name,
    int PriceCents,
    string Currency,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset SyncedAtUtc)
{
    public bool IsSellable => Status.Equals("active", StringComparison.OrdinalIgnoreCase) && PriceCents > 0;
}
