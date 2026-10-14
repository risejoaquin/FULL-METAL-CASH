using SolidPOS.PosCore.Application.Abstractions;

namespace SolidPOS.PosCore.Application.Cash;

/// <summary>Close counts represent physical cash in centavos, not cash tendered.</summary>
public sealed record AtomicCashShiftCloseRequest(
    Guid ShiftId, Guid SessionId, int CountedCashCents);

public sealed record AtomicCashShiftCloseResult(
    Guid ShiftId, int ExpectedCashCents, int CountedCashCents,
    int DifferenceCents, int CashSalesCents, int CashInCents,
    int CashOutCents, bool AlreadyClosed);

public interface IAtomicCashShiftCloseStore
{
    Task<AtomicCashShiftCloseResult> CloseAsync(
        AtomicCashShiftCloseRequest request, DateTimeOffset closedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Financial close is calculated and authorized inside the store transaction.
/// No local-only UI counter is trusted as the expected balance.
/// </summary>
public sealed class AtomicCashShiftCloseService
{
    private readonly IAtomicCashShiftCloseStore store;
    private readonly IClock clock;

    public AtomicCashShiftCloseService(IAtomicCashShiftCloseStore store, IClock clock)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<AtomicCashShiftCloseResult> CloseAsync(
        AtomicCashShiftCloseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShiftId == Guid.Empty || request.SessionId == Guid.Empty)
            throw new InvalidOperationException("Valid shift and local session IDs are required.");
        if (request.CountedCashCents < 0)
            throw new InvalidOperationException("Physically counted cash cannot be negative.");
        return store.CloseAsync(request, clock.UtcNow, cancellationToken);
    }
}
