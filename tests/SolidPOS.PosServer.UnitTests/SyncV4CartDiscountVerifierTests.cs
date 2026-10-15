using SolidPOS.DiscountEngine;
using SolidPOS.PosServer.Infrastructure.Sales;
using Xunit;

namespace SolidPOS.PosServer.UnitTests;

public sealed class SyncV4CartDiscountVerifierTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();
    private static readonly Guid Cashier = Guid.NewGuid();
    private static readonly Guid Sale = Guid.NewGuid();
    private static readonly Guid Rule = Guid.NewGuid();

    [Theory]
    [InlineData("percentage", "15")]
    [InlineData("fixed_amount", "50")]
    [InlineData("percentage", "50")]
    public void Accepts_only_exact_cart_allocations_generated_by_pos(string kind, string amount)
    {
        var gross = new long[] { 201, 103, 17, 1 };
        decimal value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var quote = DiscountCalculator.Quote(new DiscountQuoteRequest(
            Tenant, Store, Cashier, gross.Select((g, i) => new DiscountLine(
                DiscountCalculator.GetStableLineId(Sale, i), g)).ToArray(),
            [new DiscountRule(Rule, "cart", kind, value)], []));
        var submitted = quote.Lines.Select(x =>
            new SyncV4CartDiscountVerifier.SaleLine(x.GrossCents, x.CartDiscountCents)).ToArray();
        Assert.True(SyncV4CartDiscountVerifier.Matches(
            Tenant, Store, Cashier, Sale, Rule, kind, value, submitted));
        Assert.Equal(gross.Sum(), submitted.Sum(x => x.GrossCents));
        Assert.Equal(quote.DiscountCents, submitted.Sum(x => x.ClientDiscountCents));
    }

    [Fact]
    public void Rejects_redistributed_discount_even_when_total_matches()
    {
        var quote = DiscountCalculator.Quote(new DiscountQuoteRequest(
            Tenant, Store, Cashier,
            [new(DiscountCalculator.GetStableLineId(Sale, 0), 100),
             new(DiscountCalculator.GetStableLineId(Sale, 1), 100)],
            [new DiscountRule(Rule, "cart", "fixed_amount", 51)], []));
        long first = quote.Lines[0].CartDiscountCents;
        long second = quote.Lines[1].CartDiscountCents;
        var wrong = new[]
        {
            new SyncV4CartDiscountVerifier.SaleLine(100, first + 1),
            new SyncV4CartDiscountVerifier.SaleLine(100, second - 1)
        };
        Assert.False(SyncV4CartDiscountVerifier.Matches(
            Tenant, Store, Cashier, Sale, Rule, "fixed_amount", 51, wrong));
        Assert.Equal(51, wrong.Sum(x => x.ClientDiscountCents));
    }

    [Fact]
    public void Rejects_modified_server_price_or_forged_discount_amount()
    {
        var wrong = new[]
        {
            new SyncV4CartDiscountVerifier.SaleLine(200, 90),
            new SyncV4CartDiscountVerifier.SaleLine(300, 10)
        };
        Assert.False(SyncV4CartDiscountVerifier.Matches(
            Tenant, Store, Cashier, Sale, Rule, "percentage", 10, wrong));
    }

    [Fact]
    public void Rejects_zero_or_missing_identity_and_negative_or_excessive_values()
    {
        Assert.False(SyncV4CartDiscountVerifier.Matches(
            Guid.Empty, Store, Cashier, Sale, Rule, "percentage", 10,
            [new(100, 10)]));
        Assert.False(SyncV4CartDiscountVerifier.Matches(
            Tenant, Store, Cashier, Sale, Rule, "percentage", 10,
            [new(100, 101)]));
        Assert.False(SyncV4CartDiscountVerifier.Matches(
            Tenant, Store, Cashier, Sale, Rule, "percentage", 10,
            [new(-100, 0)]));
    }
}
