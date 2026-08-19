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
        if (sale.TotalCents < 0) throw new InvalidOperationException("Sale total cannot be negative.");
        if (sale.PaidCents != sale.TotalCents) throw new InvalidOperationException("Paid amount must match sale total for offline MVP.");
    }
}
