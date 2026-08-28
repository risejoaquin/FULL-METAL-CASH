using SolidPOS.PosCore.Application.Cash;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class LocalCashCalculatorTests
{
    [Fact]
    public void CalculateChangeCents_returns_tendered_minus_total()
    {
        int change = LocalCashCalculator.CalculateChangeCents(4500, 5000);

        Assert.Equal(500, change);
    }

    [Fact]
    public void CalculateChangeCents_rejects_insufficient_cash()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LocalCashCalculator.CalculateChangeCents(4500, 4000));

        Assert.Contains("insufficient", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalculateExpectedCashCents_combines_opening_sales_in_and_out()
    {
        int expected = LocalCashCalculator.CalculateExpectedCashCents(10000, 4500, 1000, 500);

        Assert.Equal(15000, expected);
    }
}
