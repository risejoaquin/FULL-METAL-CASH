using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.OfflineSales;

/// <summary>
/// The caller allocates a stable LocalSaleId and LocalPaymentId before submitting.
/// A retry after an ambiguous result must reuse the same sale, not create another.
/// </summary>
public sealed record AtomicCashCheckoutRequest(
    OfflineSaleDraft Sale,
    Guid SessionId,
    Guid CashShiftId,
    int TenderedCents,
    Guid ReceiptId,
    Guid PrintJobId);

public sealed record AtomicCashCheckoutResult(
    Guid LocalSaleId,
    Guid CashShiftId,
    Guid OutboxEventId,
    Guid PrintJobId,
    string ReceiptNumber,
    int TotalCents,
    int TenderedCents,
    int ChangeCents,
    bool AlreadyCommitted);

public interface IAtomicCashCheckoutStore
{
    Task<AtomicCashCheckoutResult> CommitAsync(
        AtomicCashCheckoutRequest request,
        LocalOutboxEvent outboxEvent,
        IReadOnlyCollection<LocalInventoryMovement> inventoryMovements,
        DateTimeOffset committedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application entry point. No network or printing is performed during the
/// financial commit. The store enforces session, permissions and prices again
/// inside the same SQLite transaction to close TOCTOU windows.
/// </summary>
public sealed class AtomicCashCheckoutService
{
    private readonly ILocalPosRepository repository;
    private readonly IAtomicCashCheckoutStore store;
    private readonly IClock clock;

    public AtomicCashCheckoutService(
        ILocalPosRepository repository, IAtomicCashCheckoutStore store, IClock clock)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<AtomicCashCheckoutResult> CheckoutAsync(
        AtomicCashCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Sale);
        OfflineSaleCalculator.Validate(request.Sale);
        if (request.Sale.CashierUserId is null || request.Sale.CashierUserId == Guid.Empty)
            throw new InvalidOperationException("Cashier identity is required.");
        if (request.Sale.LocalSaleId == Guid.Empty || request.SessionId == Guid.Empty ||
            request.CashShiftId == Guid.Empty || request.ReceiptId == Guid.Empty ||
            request.PrintJobId == Guid.Empty)
            throw new InvalidOperationException("Checkout identities cannot be empty.");
        if (request.Sale.Payments.Count != 1 ||
            !string.Equals(request.Sale.Payments[0].MethodCode, "cash", StringComparison.OrdinalIgnoreCase) ||
            request.Sale.Payments[0].LocalPaymentId is null ||
            request.Sale.Payments[0].LocalPaymentId == Guid.Empty)
            throw new InvalidOperationException("MVP checkout only supports a single cash payment with stable ID.");
        if (request.Sale.TipCents != 0)
            throw new InvalidOperationException("Tips are not enabled for MVP cash checkout.");
        if (request.TenderedCents < request.Sale.TotalCents)
            throw new InvalidOperationException("Insufficient cash tendered.");

        // Reuse the established schema v4 event builder, never invent a new sync payload.
        var outboxEvent = new OfflineSaleService(repository, clock).BuildSaleCompletedEvent(request.Sale);
        var movements = await new LocalInventoryConsumptionService(repository, clock)
            .BuildMovementsAsync(request.Sale, cancellationToken).ConfigureAwait(false);
        return await store.CommitAsync(
            request, outboxEvent, movements, clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }
}
