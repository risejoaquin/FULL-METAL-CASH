namespace SolidPOS.PosCore.Application.Storage;

/// <summary>
/// Explicit, scoped and bounded recovery of abandoned SQLite outbox claims.
/// The caller must have permission to operate on the requested terminal.
/// Nothing in this contract schedules automatic recovery or authorizes a deployment.
/// </summary>
public interface IControlledLocalSyncRecoveryRepository
{
    Task RegisterRecoveryApprovalAsync(LocalSyncRecoveryApprovalRequest request, CancellationToken cancellationToken = default);

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
    string? ApprovalReference = null,
    Guid ExecutorSessionId = default);

public sealed record LocalSyncRecoveryResult(
    Guid RecoveryId,
    bool DryRun,
    int EligibleCount,
    int RecoveredCount,
    bool HasMoreEligible,
    IReadOnlyList<Guid> CandidateEventIds);

public sealed record LocalSyncRecoveryApprovalRequest(
    Guid RecoveryId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid ApproverSessionId,
    TimeSpan ProcessingTimeout,
    int MaxEvents,
    string Reason,
    string ApprovalReference);

/// <summary>
/// Local-only permission codes, never a change to schemaVersion=4 or remote sync contract.
/// These permissions must be explicitly provisioned and verified before operational use.
/// </summary>
public static class LocalSyncRecoveryPermissions
{
    public const string Execute = "sync.recovery.execute";
    public const string Approve = "sync.recovery.approve";
}
