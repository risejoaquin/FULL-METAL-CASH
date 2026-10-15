using SolidPOS.DiscountEngine;
using Xunit;

namespace SolidPOS.PosServer.UnitTests;

public sealed class DiscountEngineParityTests
{
    [Theory]
    [InlineData(5, "percentage", "50", 3)]
    [InlineData(999, "percentage", "17.5", 175)]
    [InlineData(1, "percentage", "50", 1)]
    [InlineData(3, "fixed_amount", "100", 3)]
    [InlineData(1000, "fixed_amount", "1", 1)]
    public void Legacy_server_validation_and_shared_quote_use_exact_same_cents(
        long gross, string kind, string amount, long expected)
    {
        var discountId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        decimal parsed = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        long single = DiscountCalculator.CalculateAmountCents(gross, kind, parsed);
        var quote = DiscountCalculator.Quote(new DiscountQuoteRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [new DiscountLine(lineId, gross)],
            [new DiscountRule(discountId, "line", kind, parsed, lineId)],
            []));
        Assert.Equal(expected, single);
        Assert.Equal(single, quote.DiscountCents);
        Assert.Equal(quote.GrossCents, quote.NetCents + quote.DiscountCents);
    }

    [Fact]
    public void Cart_allocation_financial_parity_for_receipts_and_refunds()
    {
        var lines = Enumerable.Range(1, 37)
            .Select(i => new DiscountLine(Guid.NewGuid(), i * 179))
            .ToArray();
        var discounted = DiscountCalculator.Quote(new DiscountQuoteRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            lines,
            [new DiscountRule(Guid.NewGuid(), "cart", "percentage", 17.25m)],
            []));
        Assert.Equal(discounted.GrossCents, discounted.NetCents + discounted.DiscountCents);
        Assert.Equal(discounted.NetCents, discounted.Lines.Sum(x => x.NetCents));
        Assert.Equal(discounted.DiscountCents, discounted.Allocations.Sum(x => x.Cents));
        Assert.All(discounted.Lines, line =>
            Assert.True(line.NetCents >= 0 && line.NetCents <= line.GrossCents));
    }
}
