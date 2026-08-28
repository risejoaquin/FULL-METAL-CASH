using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Terminals;

public static class TerminalBindingValidator
{
    public static void Validate(TerminalBinding binding)
    {
        if (binding.TenantId == Guid.Empty) throw new InvalidOperationException("TenantId is required.");
        if (binding.StoreId == Guid.Empty) throw new InvalidOperationException("StoreId is required.");
        if (binding.TerminalId == Guid.Empty) throw new InvalidOperationException("TerminalId is required.");
        if (string.IsNullOrWhiteSpace(binding.TerminalFingerprint)) throw new InvalidOperationException("Terminal fingerprint is required.");
        if (string.IsNullOrWhiteSpace(binding.TerminalToken)) throw new InvalidOperationException("Terminal token is required.");
        if (binding.SchemaVersion <= 0) throw new InvalidOperationException("Schema version must be positive.");
    }
}
