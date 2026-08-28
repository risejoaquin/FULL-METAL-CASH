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
