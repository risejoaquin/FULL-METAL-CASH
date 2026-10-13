namespace SolidPOS.PosCore.Application.Storage;

/// <summary>
/// Explicit, scoped and bounded recovery of abandoned SQLite outbox claims.
/// The caller must have permission to operate on the requested terminal.
/// Nothing in this contract schedules automatic recovery or authorizes a deployment.
/// </summary>
public interface IControlledLocalSyncRecoveryRepository
{
    Task<LocalSyncRecoveryResult> RecoverStaleClaimsWithAuditAsync(
        LocalSyncRecoveryRequest request, CancellationToken cancellationToken = default);
}

public sealed record LocalSyncRecoveryRequest(
    Guid RecoveryId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    DateTimeOffset NowUtc,
    TimeSpan ProcessingTimeout,
    int MaxEvents,
    string Actor,
    string Reason,
    bool DryRun,
    string? ApprovalReference = null);

public sealed record LocalSyncRecoveryResult(
    Guid RecoveryId,
    bool DryRun,
    int EligibleCount,
    int RecoveredCount,
    bool HasMoreEligible,
    IReadOnlyList<Guid> CandidateEventIds);
