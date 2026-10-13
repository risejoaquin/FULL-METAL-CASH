using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.OfflineSales;

public static class OfflineSaleCalculator
{
    public static void Validate(OfflineSaleDraft sale)
    {
        if (sale.TenantId == Guid.Empty) throw new InvalidOperationException("TenantId is required.");
        if (sale.StoreId == Guid.Empty) throw new InvalidOperationException("StoreId is required.");
        if (sale.TerminalId == Guid.Empty) throw new InvalidOperationException("TerminalId is required.");
        if (sale.Lines.Count == 0) throw new InvalidOperationException("Sale must contain at least one line.");
        if (sale.Payments.Count == 0) throw new InvalidOperationException("Sale must contain at least one payment.");
        if (sale.Lines.Any(line => line.Quantity <= 0)) throw new InvalidOperationException("Line quantity must be positive.");
        if (sale.Lines.Any(line => line.UnitPriceCents < 0)) throw new InvalidOperationException("Unit price cannot be negative.");
        // Guard before accessing int-based record totals: unchecked integer
        // multiplication, discount tampering or receipt/payment overflow must
        // never turn an offline sale into a financially inconsistent event.
        long net = 0;
        foreach (OfflineSaleLineDraft line in sale.Lines)
        {
            long gross = checked((long)line.Quantity * line.UnitPriceCents);
            if (line.DiscountCents < 0 || line.DiscountCents > gross)
                throw new InvalidOperationException("Line discount must be within gross amount.");
            if (line.DiscountCents > 0 && (!line.DiscountId.HasValue || line.DiscountId == Guid.Empty))
                throw new InvalidOperationException("Discounted lines require a valid rule reference.");
            if (line.DiscountId == Guid.Empty)
                throw new InvalidOperationException("Discount rule identifier cannot be empty.");
            if (gross > int.MaxValue)
                throw new InvalidOperationException("Offline line gross exceeds supported cent range.");
            net = checked(net + gross - line.DiscountCents);
        }
        if (sale.TipCents < 0 || sale.TipCents > int.MaxValue ||
            checked(net + sale.TipCents) > int.MaxValue)
            throw new InvalidOperationException("Offline sale amount exceeds supported cent range.");
        if (sale.Payments.Any(payment => payment.AmountCents < 0))
            throw new InvalidOperationException("Payments must not be negative.");
        long paid = 0;
        foreach (OfflineSalePaymentDraft payment in sale.Payments)
            paid = checked(paid + payment.AmountCents);
        if (paid != checked(net + sale.TipCents))
            throw new InvalidOperationException("Paid amount must match sale total for offline MVP.");
        if (sale.TotalCents != net + sale.TipCents || sale.PaidCents != paid)
            throw new InvalidOperationException("Offline sale total does not reconcile with line and payment amounts.");
    }
}
