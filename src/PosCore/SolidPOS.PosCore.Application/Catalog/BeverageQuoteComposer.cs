using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Catalog;

/// <summary>
/// Computes a deterministic, local-only beverage quote using the existing
/// sellable catalog and OfflineSaleLineDraft contracts. This does not charge,
/// reserve inventory, persist a sale, or certify that toppings are modifiers.
/// </summary>
public static class BeverageQuoteComposer
{
    public static BeverageQuote Compose(
        LocalCatalogProduct beverage,
        int beverageQuantity,
        IReadOnlyList<BeverageExtraSelection> extras)
    {
        ArgumentNullException.ThrowIfNull(beverage);
        ArgumentNullException.ThrowIfNull(extras);
        if (beverageQuantity <= 0)
            throw new InvalidOperationException("Beverage quantity must be positive.");

        ValidateProduct(beverage);
        var usedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { beverage.Sku };
        // A renamed SKU must not allow the same catalog identity to be billed twice.
        var usedCatalogIdentities = new HashSet<(Guid ProductId, Guid? VariantId)>
        {
            (beverage.ProductId, beverage.VariantId)
        };
        var lines = new List<OfflineSaleLineDraft>();
        var links = new List<BeverageExtraLink>();
        long total = checked((long)beverageQuantity * beverage.PriceCents);
        EnsureSupportedTotal(total);
        lines.Add(ToLine(beverage, beverageQuantity));

        foreach (var extra in extras)
        {
            if (extra is null || extra.Product is null)
                throw new InvalidOperationException("An extra must reference a catalog product.");
            ValidateProduct(extra.Product);
            if (extra.QuantityPerBeverage <= 0)
                throw new InvalidOperationException("Extra quantity per beverage must be positive.");
            if (extra.Product.ProductId == beverage.ProductId ||
                !usedSkus.Add(extra.Product.Sku) ||
                !usedCatalogIdentities.Add((extra.Product.ProductId, extra.Product.VariantId)))
                throw new InvalidOperationException("Duplicate or self-referential extra catalog identity.");

            long requestedQuantity = checked((long)beverageQuantity * extra.QuantityPerBeverage);
            if (requestedQuantity > int.MaxValue)
                throw new InvalidOperationException("Extra quantity exceeds supported range.");
            long extraTotal = checked(requestedQuantity * extra.Product.PriceCents);
            total = checked(total + extraTotal);
            EnsureSupportedTotal(total);
            lines.Add(ToLine(extra.Product, (int)requestedQuantity));
            links.Add(new BeverageExtraLink(beverage.Sku, extra.Product.Sku, (int)requestedQuantity));
        }

        return new BeverageQuote(lines.AsReadOnly(), (int)total, links.AsReadOnly());
    }

    private static OfflineSaleLineDraft ToLine(LocalCatalogProduct product, int quantity) =>
        new(product.ProductId, product.VariantId, product.Sku, product.Name,
            quantity, product.PriceCents);

    private static void ValidateProduct(LocalCatalogProduct product)
    {
        if (product.ProductId == Guid.Empty || product.VariantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(product.Sku) || product.Sku != product.Sku.Trim() ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceCents <= 0 ||
            !string.Equals(product.Currency, "MXN", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(product.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Catalog product is not sellable for an MXN beverage quote.");
    }

    private static void EnsureSupportedTotal(long cents)
    {
        if (cents <= 0 || cents > int.MaxValue)
            throw new InvalidOperationException("Beverage quote exceeds supported cent range.");
    }
}

public sealed record BeverageExtraSelection(LocalCatalogProduct Product, int QuantityPerBeverage);

/// <summary>
/// Quote-only grouping metadata. Flattened OfflineSaleLineDraft entries do not
/// establish a durable parent/modifier relationship in the frozen sale contract.
/// </summary>
public sealed record BeverageExtraLink(string BeverageSku, string ExtraSku, int Quantity);

public sealed record BeverageQuote(
    IReadOnlyList<OfflineSaleLineDraft> Lines,
    int TotalCents,
    IReadOnlyList<BeverageExtraLink> ExtraLinks);
