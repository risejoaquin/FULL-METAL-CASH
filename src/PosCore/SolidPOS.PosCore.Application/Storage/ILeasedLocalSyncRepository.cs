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

}
