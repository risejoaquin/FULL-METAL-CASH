using SolidPOS.DiscountEngine;

namespace SolidPOS.PosServer.Infrastructure.Sales;

/// <summary>
/// Verifies legacy schema_version_4 line allocations as ONE global cart
/// discount. An approved, tenant-scoped PostgreSQL discount and trusted
/// product/variant gross prices must be supplied by the sales repository.
/// </summary>
public static class SyncV4CartDiscountVerifier
{
    public sealed record SaleLine(long GrossCents, long ClientDiscountCents);

    public static bool Matches(
        Guid tenantId, Guid storeId, Guid cashierUserId, Guid localSaleId,
        Guid discountId, string type, decimal amount,
        IReadOnlyList<SaleLine> lines)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty ||
            cashierUserId == Guid.Empty || localSaleId == Guid.Empty ||
            discountId == Guid.Empty || lines is null || lines.Count is < 1 or > 1000 ||
            lines.Any(line => line.GrossCents < 0 ||
                line.ClientDiscountCents < 0 ||
                line.ClientDiscountCents > line.GrossCents))
            return false;
        try
        {
            DiscountQuoteResult quote = DiscountCalculator.Quote(new DiscountQuoteRequest(
                tenantId, storeId, cashierUserId,
                lines.Select((line, index) => new DiscountLine(
                    DiscountCalculator.GetStableLineId(localSaleId, index),
                    line.GrossCents)).ToArray(),
                [new DiscountRule(discountId, "cart", type, amount)],
                []));
            return quote.Lines.Count == lines.Count &&
                quote.Lines.Select((line, index) =>
                    line.CartDiscountCents == lines[index].ClientDiscountCents).All(x => x);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            return false;
        }
    }
}
