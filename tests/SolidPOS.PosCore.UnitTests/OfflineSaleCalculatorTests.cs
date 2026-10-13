using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class OfflineSaleCalculatorTests
{
    [Fact]
    public void Validate_accepts_balanced_offline_sale()
    {
        var sale = CreateSale(totalPaidCents: 4500);

        OfflineSaleCalculator.Validate(sale);

        Assert.Equal(4500, sale.TotalCents);
        Assert.Equal(4500, sale.PaidCents);
    }

    [Fact]
    public void Validate_rejects_unbalanced_payment()
    {
        var sale = CreateSale(totalPaidCents: 4000);

        var error = Assert.Throws<InvalidOperationException>(() => OfflineSaleCalculator.Validate(sale));

        Assert.Contains("Paid amount", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_negative_or_over_gross_discounts_before_persistence()
    {
        var baseline = CreateSale(totalPaidCents: 4500);
        foreach (int altered in new[] { -1, 4501 })
        {
            var tampered = baseline with
            {
                Lines = [baseline.Lines[0] with
                {
                    DiscountCents = altered,
                    DiscountId = Guid.NewGuid()
                }]
            };
            Assert.Throws<InvalidOperationException>(() =>
                OfflineSaleCalculator.Validate(tampered));
        }
    }

    [Fact]
    public void Validate_refuses_discount_without_a_rule_reference()
    {
        var original = CreateSale(totalPaidCents: 4490);
        var attempted = original with
        {
            Lines = [original.Lines[0] with { DiscountCents = 10 }]
        };
        Assert.Throws<InvalidOperationException>(() =>
            OfflineSaleCalculator.Validate(attempted));
    }

    [Fact]
    public void Validate_refuses_large_quantities_and_payment_overflow()
    {
        var original = CreateSale(totalPaidCents: 4500);
        var extreme = original with
        {
            Lines = [original.Lines[0] with { Quantity = int.MaxValue }]
        };
        Assert.Throws<InvalidOperationException>(() =>
            OfflineSaleCalculator.Validate(extreme));

        var paymentOverflow = original with
        {
            Payments =
            [
                new OfflineSalePaymentDraft("cash", int.MaxValue),
                new OfflineSalePaymentDraft("transfer", int.MaxValue)
            ]
        };
        Assert.Throws<InvalidOperationException>(() =>
            OfflineSaleCalculator.Validate(paymentOverflow));
    }

    private static OfflineSaleDraft CreateSale(int totalPaidCents) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        new[] { new OfflineSaleLineDraft(Guid.NewGuid(), null, "QSR-AMERICANO", "Americano 12oz", 1, 4500) },
        new[] { new OfflineSalePaymentDraft("cash", totalPaidCents) },
        "MXN");
}
