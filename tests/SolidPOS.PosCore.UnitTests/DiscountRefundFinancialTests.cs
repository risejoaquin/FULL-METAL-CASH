using SolidPOS.DiscountEngine;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class DiscountRefundFinancialTests
{
    [Fact]
    public void Three_partial_refunds_restore_exact_sale_total_without_rounding_drift()
    {
        var id = Guid.NewGuid();
        long reversedNet = 0;
        long reversedDiscount = 0;
        long reversedGross = 0;
        for (int i = 0; i < 3; i++)
        {
            var quote = DiscountRefundCalculator.Quote([
                new DiscountRefundLine(id, 3, i, 1, 101, 67)
            ]);
            reversedNet += quote.RefundNetCents;
            reversedDiscount += quote.DiscountReversedCents;
            reversedGross += quote.GrossReversedCents;
        }
        Assert.Equal(67, reversedNet);
        Assert.Equal(34, reversedDiscount);
        Assert.Equal(101, reversedGross);
    }

    [Fact]
    public void Less_than_one_cent_per_unit_does_not_create_negative_refund()
    {
        Guid line = Guid.NewGuid();
        long totalRefund = 0, totalSavings = 0, totalGross = 0;
        for (int i = 0; i < 3; i++)
        {
            var result = DiscountRefundCalculator.Quote([
                new DiscountRefundLine(line, 3, i, 1, 2, 1)
            ]);
            Assert.True(result.RefundNetCents >= 0);
            Assert.True(result.DiscountReversedCents >= 0);
            totalRefund += result.RefundNetCents;
            totalSavings += result.DiscountReversedCents;
            totalGross += result.GrossReversedCents;
        }
        Assert.Equal(1, totalRefund);
        Assert.Equal(1, totalSavings);
        Assert.Equal(2, totalGross);
    }

    [Fact]
    public void Multiple_line_return_reconciles_exact_money()
    {
        var result = DiscountRefundCalculator.Quote([
            new DiscountRefundLine(Guid.NewGuid(), 2, 0, 1, 1200, 900),
            new DiscountRefundLine(Guid.NewGuid(), 3, 1, 2, 300, 200)
        ]);
        Assert.Equal(result.GrossReversedCents,
            result.RefundNetCents + result.DiscountReversedCents);
        Assert.Equal(result.RefundNetCents, result.Lines.Sum(x => x.RefundNetCents));
        Assert.Equal(result.DiscountReversedCents, result.Lines.Sum(x => x.DiscountReversedCents));
    }

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(3, 1, 3)]
    [InlineData(3, -1, 1)]
    [InlineData(0, 0, 1)]
    public void Invalid_or_double_return_is_rejected(int sold, int previous, int current)
    {
        Assert.Throws<InvalidOperationException>(() => DiscountRefundCalculator.Quote([
            new DiscountRefundLine(Guid.NewGuid(), sold, previous, current, 100, 90)
        ]));
    }

    [Fact]
    public void Returning_no_more_than_remaining_units_is_mandatory()
    {
        var id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => DiscountRefundCalculator.Quote([
            new DiscountRefundLine(id, 5, 4, 2, 999, 799)
        ]));
    }
}
