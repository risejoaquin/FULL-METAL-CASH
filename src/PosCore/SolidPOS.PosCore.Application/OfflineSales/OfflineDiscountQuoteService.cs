using SolidPOS.DiscountEngine;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.OfflineSales;

/// <summary>
/// Read-only, offline quote using trusted cached rules. The quote is not a
/// completed sale or an authorization token: settlement still requires
/// server-side rule verification against the unchanged sync-v4 contract.
/// </summary>
public static class OfflineDiscountQuoteService
{
    public static DiscountQuoteResult Preview(
        OfflineSaleDraft sale, IReadOnlyList<DiscountRule> cachedRules,
        IReadOnlyList<DiscountApproval> verifiedApprovals)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(cachedRules);
        ArgumentNullException.ThrowIfNull(verifiedApprovals);
        if (sale.LocalSaleId == Guid.Empty || sale.TenantId == Guid.Empty ||
            sale.StoreId == Guid.Empty || sale.TerminalId == Guid.Empty ||
            !sale.CashierUserId.HasValue || sale.CashierUserId == Guid.Empty ||
            sale.Lines.Count is < 1 or > 1000)
            throw new InvalidOperationException("Offline quote requires identified sale, cashier and terminal.");

        var lines = new List<DiscountLine>(sale.Lines.Count);
        for (int i = 0; i < sale.Lines.Count; i++)
        {
            OfflineSaleLineDraft line = sale.Lines[i];
            if (line.ProductId == Guid.Empty || line.Quantity <= 0 || line.UnitPriceCents < 0 ||
                line.DiscountCents != 0 || line.DiscountId.HasValue)
                throw new InvalidOperationException("Preview requires valid pre-discount sale lines.");
            long gross = checked((long)line.Quantity * line.UnitPriceCents);
            lines.Add(new DiscountLine(GetStableLineId(sale.LocalSaleId, i), gross));
        }

        return DiscountCalculator.Quote(new DiscountQuoteRequest(
            sale.TenantId, sale.StoreId, sale.CashierUserId.Value,
            lines, cachedRules, verifiedApprovals));
    }

    /// <summary>
    /// Materialize ONLY one globally scoped cart rule into existing v4 line
    /// discount fields. The server independently verifies exact allocation.
    /// Approval-required rules are refused until authenticated offline
    /// supervisor authorization exists end-to-end.
    /// </summary>
    internal static OfflineSaleDraft ApplyCartDiscount(
        OfflineSaleDraft sale, DiscountRule trustedCachedRule,
        IReadOnlyList<OfflineSalePaymentDraft> payments)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(trustedCachedRule);
        ArgumentNullException.ThrowIfNull(payments);
        if (trustedCachedRule.Scope != "cart" || trustedCachedRule.LineId.HasValue ||
            trustedCachedRule.RequiresApproval)
            throw new UnauthorizedAccessException(
                "Only non-approval cart rules can be finalized through the current v4 contract.");
        if (sale.Lines.Any(line => line.DiscountId.HasValue || line.DiscountCents != 0))
            throw new InvalidOperationException("Cannot combine cart discount with existing line discounts in v4.");

        DiscountQuoteResult quote = Preview(sale, [trustedCachedRule], []);
        long totalDue = checked(quote.NetCents + sale.TipCents);
        if (totalDue < 0 || totalDue > int.MaxValue ||
            quote.Lines.Any(x => x.CartDiscountCents > int.MaxValue) ||
            payments.Count == 0 ||
            payments.Any(x => x.AmountCents < 0) ||
            payments.Sum(x => (long)x.AmountCents) != totalDue)
            throw new InvalidOperationException("Payment must equal the discounted total including tips.");

        var updatedLines = sale.Lines.Select((line, index) =>
            line with
            {
                DiscountId = trustedCachedRule.Id,
                DiscountCents = checked((int)quote.Lines[index].CartDiscountCents)
            }).ToArray();
        var completed = sale with { Lines = updatedLines, Payments = payments };
        OfflineSaleCalculator.Validate(completed);
        return completed;
    }

    /// <summary>Stable line identity across restarts without changing the v4 payload.</summary>
    public static Guid GetStableLineId(Guid localSaleId, int zeroBasedLineIndex) =>
        DiscountCalculator.GetStableLineId(localSaleId, zeroBasedLineIndex);
}
