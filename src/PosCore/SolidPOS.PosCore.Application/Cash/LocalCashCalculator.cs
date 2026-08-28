namespace SolidPOS.PosCore.Application.Cash;

public static class LocalCashCalculator
{
    public static int CalculateChangeCents(int totalCents, int tenderedCents)
    {
        if (totalCents < 0) throw new ArgumentOutOfRangeException(nameof(totalCents), "Sale total cannot be negative.");
        if (tenderedCents < totalCents) throw new InvalidOperationException($"Tendered cash is insufficient. totalCents={totalCents}; tenderedCents={tenderedCents}.");
        return tenderedCents - totalCents;
    }

    public static int CalculateExpectedCashCents(int openingAmountCents, int cashSalesCents, int cashInCents, int cashOutCents)
    {
        if (openingAmountCents < 0) throw new ArgumentOutOfRangeException(nameof(openingAmountCents), "Opening amount cannot be negative.");
        if (cashSalesCents < 0) throw new ArgumentOutOfRangeException(nameof(cashSalesCents), "Cash sales cannot be negative.");
        if (cashInCents < 0) throw new ArgumentOutOfRangeException(nameof(cashInCents), "Cash in cannot be negative.");
        if (cashOutCents < 0) throw new ArgumentOutOfRangeException(nameof(cashOutCents), "Cash out cannot be negative.");
        return openingAmountCents + cashSalesCents + cashInCents - cashOutCents;
    }
}
