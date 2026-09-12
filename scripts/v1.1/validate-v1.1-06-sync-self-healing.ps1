param(
    [switch]$StaticOnly
)

$ErrorActionPreference = 'Stop'

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Contains([string]$Path, [string[]]$Terms) {
    Assert-True (Test-Path $Path) "Required file missing: $Path"
    $content = Get-Content $Path -Raw
    foreach ($term in $Terms) {
        Assert-True ($content.Contains($term)) "Required contract term '$term' missing from $Path"
    }
}

$repo = (Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) '..\..')).Path
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_06_SYNC_SELF_HEALING.md'
$manifestPath = Join-Path $repo 'V1_1_06_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_06_VALIDATION_COMMANDS.md'
$outboxPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Domain\OutboxEvent.cs'
$repositoryPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Storage\ILocalPosRepository.cs'
$policyPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Sync\LocalSyncRetryPolicy.cs'
$pushServicePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Sync\RemoteSyncPushService.cs'
$sqlitePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Infrastructure\SQLite\SQLiteLocalPosRepository.cs'
$testsPath = Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\SyncSelfHealingTests.cs'
$workflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $docPath @(
    'Sync Self-Healing Contract',
    'retry_pending',
    'dead_letter',
    'bounded retry attempts',
    'backoff plus jitter',
    'preserve original event identity',
    'schemaVersion: 4',
    'Queue health summary'
)
Assert-Contains $manifestPath @('V1.1-06 Sync Self-Healing', '23beaacb884a92a9a944aa96a2a9d4f88e324c53', 'schemaVersion: 4')
Assert-Contains $commandsPath @('validate-v1.1-06-sync-self-healing.ps1', 'SyncSelfHealingTests', 'RemoteSyncPushServiceTests', 'LocalOutboxBatchPlannerTests')
Assert-Contains $outboxPath @('RetryPending = 5', 'LocalSyncQueueHealthSummary', 'RequiresRecovery')
Assert-Contains $repositoryPath @('MarkOutboxRetryPendingAsync', 'MarkOutboxDeadLetterAsync', 'RecoverRetryPendingOutboxEventsAsync', 'GetLocalSyncQueueHealthAsync')
Assert-Contains $policyPath @('LocalSyncRetryPolicy', 'DefaultMaxAttempts', 'StableJitterMilliseconds', 'EvaluateException', 'EvaluateRemoteFailure')
Assert-Contains $pushServicePath @('ApplyRetryDecisionAsync', 'MarkOutboxRetryPendingAsync', 'MarkOutboxDeadLetterAsync')
Assert-Contains $sqlitePath @('LocalOutboxStatus.RetryPending', 'GetLocalSyncQueueHealthAsync', 'RecoverRetryPendingOutboxEventsAsync', 'hasStuckProcessing')
Assert-Contains $testsPath @(
    'Transient_failure_moves_event_to_retry_pending_with_backoff_and_jitter',
    'Retry_policy_backoff_increases_and_jitter_exists',
    'Retry_pending_event_recovers_to_pending_and_syncs_without_duplicate_side_effect',
    'Poison_event_moves_to_dead_letter_and_remains_visible',
    'Duplicate_acknowledgement_is_idempotent',
    'Queue_health_summary_reports_retry_dead_letter_and_stuck_processing',
    'Batch_planner_preserves_tenant_isolation'
)
Assert-Contains $workflowPath @('V1.1-06 sync self-healing static contract', 'validate-v1.1-06-sync-self-healing.ps1 -StaticOnly')

Write-Host 'PASS V1.1-06 SYNC SELF-HEALING STATIC CONTRACT'
if ($StaticOnly) { exit 0 }
