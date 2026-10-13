namespace SolidPOS.DiscountEngine;

/// <summary>
/// Cumulative refundable basis for a single sold line, in integer cents.
/// Previous units must be the quantity already refunded in an authoritative
/// ledger; this pure calculation does not authorize or issue money.
/// </summary>
public sealed record DiscountRefundLine(
    Guid LineId, int SoldQuantity, int PreviouslyRefundedQuantity,
    int ReturnQuantity, long OriginalGrossCents, long OriginalNetCents);

public sealed record DiscountRefundLineResult(
    Guid LineId, long GrossReversedCents, long DiscountReversedCents,
    long RefundNetCents, int TotalRefundedQuantity);

public sealed record DiscountRefundQuoteResult(
    long GrossReversedCents, long DiscountReversedCents,
    long RefundNetCents, IReadOnlyList<DiscountRefundLineResult> Lines);

/// <summary>
/// Cumulative rounding makes any split-refund sequence sum to exactly the
/// originally paid amount when the final unit is returned. A refund is NEVER
/// a proof of ledger balance or payment-method authorization.
/// </summary>
public static class DiscountRefundCalculator
{
    public static DiscountRefundQuoteResult Quote(IReadOnlyCollection<DiscountRefundLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count is < 1 or > 1000 ||
            lines.Select(x => x.LineId).Distinct().Count() != lines.Count)
            throw new InvalidOperationException("Refund requires unique sale lines.");

        var results = new List<DiscountRefundLineResult>();
        long totalGross = 0, totalDiscount = 0, totalNet = 0;
        foreach (var line in lines)
        {
            if (line.LineId == Guid.Empty || line.SoldQuantity <= 0 ||
                line.PreviouslyRefundedQuantity < 0 || line.ReturnQuantity <= 0 ||
                line.PreviouslyRefundedQuantity >= line.SoldQuantity ||
                line.ReturnQuantity > line.SoldQuantity - line.PreviouslyRefundedQuantity ||
                line.OriginalNetCents < 0 ||
                line.OriginalGrossCents < line.OriginalNetCents)
                throw new InvalidOperationException("Invalid partial refund or oversold refund quantity.");

            int nowRefunded = checked(line.PreviouslyRefundedQuantity + line.ReturnQuantity);
            long gross = checked(Cumulative(line.OriginalGrossCents, nowRefunded, line.SoldQuantity) -
                Cumulative(line.OriginalGrossCents, line.PreviouslyRefundedQuantity, line.SoldQuantity));
            long net = checked(Cumulative(line.OriginalNetCents, nowRefunded, line.SoldQuantity) -
                Cumulative(line.OriginalNetCents, line.PreviouslyRefundedQuantity, line.SoldQuantity));
            long saving = checked(gross - net);
            if (saving < 0 || net < 0)
                throw new InvalidOperationException("Refund does not conserve original discount.");

            results.Add(new DiscountRefundLineResult(
                line.LineId, gross, saving, net, nowRefunded));
            totalGross = checked(totalGross + gross);
            totalNet = checked(totalNet + net);
            totalDiscount = checked(totalDiscount + saving);
        }
        if (totalGross != checked(totalNet + totalDiscount))
            throw new InvalidOperationException("Refund money conservation violated.");
        return new DiscountRefundQuoteResult(totalGross, totalDiscount, totalNet, results);
    }

    private static long Cumulative(long amount, int part, int whole) =>
        checked((long)Math.Round((decimal)amount * part / whole, 0,
            MidpointRounding.AwayFromZero));
}
