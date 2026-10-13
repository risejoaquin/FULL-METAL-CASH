using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Storage;

/// <summary>
/// Opt-in durable claiming protocol for the real SQLite outbox. A claim is
/// scoped to one sync batch and one terminal. It protects against two workers
/// simultaneously transmitting the same pending event.
/// </summary>
public interface ILeasedLocalSyncRepository
{
    Task<IReadOnlyList<LocalOutboxEvent>> ClaimPendingOutboxEventsAsync(
        int limit, Guid claimId, DateTimeOffset claimedAtUtc, CancellationToken cancellationToken = default);

    Task CommitClaimedSyncAcknowledgementsAsync(
        Guid claimId, IReadOnlyCollection<LocalSyncAcknowledgement> acknowledgements,
        DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default);

    Task MarkClaimedOutboxFailureAsync(
        Guid claimId, Guid eventId, LocalOutboxStatus status, string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Releases only events still owned by this claim; never modifies another claim.</summary>
    Task ReleaseClaimAsync(Guid claimId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit recovery for abandoned claims after a conservative processing timeout.
    /// Call only when the original worker is presumed dead; server-side idempotency
    /// by stable event ID remains necessary when a slow worker was still active.
    /// </summary>
    Task<int> RecoverStaleClaimsAsync(
        DateTimeOffset nowUtc, TimeSpan processingTimeout, CancellationToken cancellationToken = default);
}
