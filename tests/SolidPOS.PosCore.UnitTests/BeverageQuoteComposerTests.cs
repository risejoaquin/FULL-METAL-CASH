using SolidPOS.PosCore.Application.Catalog;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class BeverageQuoteComposerTests
{
    [Fact]
    public void Two_large_coffees_with_two_extras_quote_15000_cents()
    {
        var quote = BeverageQuoteComposer.Compose(
            Product("DEMO-COFFEE-16", 5900, variant: true), 2,
            [new(Product("DEMO-PEARLS", 900), 1), new(Product("DEMO-CREAM", 700), 1)]);

        Assert.Equal(15000, quote.TotalCents);
        Assert.Equal(3, quote.Lines.Count);
        Assert.Equal(new[] { 2, 2, 2 }, quote.Lines.Select(line => line.Quantity));
        Assert.Equal(15000, quote.Lines.Sum(line => line.NetCents));
        Assert.Equal("DEMO-COFFEE-16", quote.ExtraLinks[0].BeverageSku);
        Assert.Equal("DEMO-PEARLS", quote.ExtraLinks[0].ExtraSku);
    }

    [Fact]
    public void One_small_tea_with_syrup_quotes_5400_cents()
    {
        var quote = BeverageQuoteComposer.Compose(
            Product("DEMO-TEA-12", 4800, variant: true), 1,
            [new(Product("DEMO-SYRUP", 600), 1)]);

        Assert.Equal(5400, quote.TotalCents);
        Assert.Equal(2, quote.Lines.Count);
        Assert.Equal(1, quote.ExtraLinks[0].Quantity);
    }

    [Fact]
    public void Different_sizes_keep_distinct_variant_ids()
    {
        var small = Product("DEMO-COFFEE-12", 4500, variant: true);
        var large = Product("DEMO-COFFEE-16", 5900, variant: true);

        Assert.NotEqual(small.VariantId, large.VariantId);
        Assert.Equal(small.VariantId, BeverageQuoteComposer.Compose(small, 1, []).Lines[0].VariantId);
        Assert.Equal(large.VariantId, BeverageQuoteComposer.Compose(large, 1, []).Lines[0].VariantId);
    }

    [Theory]
    [InlineData("inactive", "MXN", 5900)]
    [InlineData("active", "USD", 5900)]
    [InlineData("active", "MXN", 0)]
    public void Inactive_wrong_currency_or_zero_price_fails_closed(string status, string currency, int cents)
    {
        var invalid = Product("DEMO-COFFEE-16", cents, variant: true) with
        { Status = status, Currency = currency };
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(invalid, 1, []));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(
            Product("DEMO-COFFEE-16", 5900), 1, [new(invalid, 1)]));
    }

    [Fact]
    public void Zero_negative_or_duplicate_extra_quantities_are_rejected()
    {
        var coffee = Product("DEMO-COFFEE-16", 5900);
        var pearls = Product("DEMO-PEARLS", 900);
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 0, []));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, -1, []));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1, [new(pearls, 0)]));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1, [new(pearls, -1)]));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1,
            [new(pearls, 1), new(pearls with { Sku = "demo-pearls" }, 1)]));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1, [new(coffee, 1)]));
    }

    [Fact]
    public void Whitespace_padded_extra_sku_cannot_bypass_catalog_identity()
    {
        var coffee = Product("DEMO-COFFEE-16", 5900);
        var pearls = Product("DEMO-PEARLS", 900);
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1,
            [new(pearls with { Sku = " DEMO-PEARLS " }, 1)]));
    }

    [Fact]
    public void Extreme_quantities_and_totals_fail_without_wraparound()
    {
        var coffee = Product("DEMO-COFFEE-16", 5900);
        var pearls = Product("DEMO-PEARLS", 900);
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, int.MaxValue, []));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1,
            [new(pearls, int.MaxValue)]));
        Assert.Throws<InvalidOperationException>(() => BeverageQuoteComposer.Compose(coffee, 1000,
            [new(pearls, 3_000_000)]));
    }

    [Fact]
    public void Quote_does_not_mutate_catalog_or_create_a_payment()
    {
        var coffee = Product("DEMO-COFFEE-16", 5900);
        var extra = Product("DEMO-PEARLS", 900);
        var quote = BeverageQuoteComposer.Compose(coffee, 1, [new(extra, 2)]);

        Assert.Equal(7700, quote.TotalCents);
        Assert.Equal(5900, coffee.PriceCents);
        Assert.Equal(900, extra.PriceCents);
        Assert.All(quote.Lines, line => Assert.Equal(0, line.DiscountCents));
        Assert.Equal(2, quote.ExtraLinks[0].Quantity);
    }

    private static LocalCatalogProduct Product(string sku, int priceCents, bool variant = false) =>
        new(Guid.NewGuid(), variant ? Guid.NewGuid() : null, sku, sku, priceCents,
            "MXN", "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
