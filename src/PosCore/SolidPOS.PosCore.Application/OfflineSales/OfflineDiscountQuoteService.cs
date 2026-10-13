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

    /// <summary>Stable line identity across restarts without changing the v4 payload.</summary>
    public static Guid GetStableLineId(Guid localSaleId, int zeroBasedLineIndex) =>
        DiscountCalculator.GetStableLineId(localSaleId, zeroBasedLineIndex);
}
