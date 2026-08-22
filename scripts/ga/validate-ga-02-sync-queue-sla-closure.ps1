param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[GA-02] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Assert-DocumentContainsAny { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); $ok=$false; foreach($t in $Terms){if($c.Contains($t.ToLowerInvariant())){$ok=$true}}; Assert-True $ok "Document $Path missing all accepted lifecycle terms: $($Terms -join ', ')" }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$base=$BaseUrl.TrimEnd('/')
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$ga01=Join-Path $scriptRoot 'validate-ga-01-general-availability-baseline-freeze.ps1'
$ga01Manifest=Join-Path $repoRoot '.runtime\ga-01-general-availability-baseline-freeze\ga-01-manifest.json'
$secretScan=Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1'
$checkSql=Join-Path $scriptRoot 'ga-02-sync-queue-sla-closure-check.sql'
$remediationSql=Join-Path $scriptRoot 'ga-02-close-historical-sync-validation-fixtures.sql'
$runtime=Join-Path $repoRoot '.runtime\ga-02-sync-queue-sla-closure'
$manifestPath=Join-Path $runtime 'ga-02-manifest.json'
$evidencePath=Join-Path $runtime 'ga-02-evidence.md'
$snapshotPath=Join-Path $runtime 'ga-02-snapshot.json'
$logPath=Join-Path $repoRoot 'docs\ga\logs\ga-02-sync-queue-sla-closure-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
 (Join-Path $repoRoot 'SOLIDPOS_GA_02_SYNC_QUEUE_AND_SLA_CLOSURE.md'),
 (Join-Path $repoRoot 'docs\ga\ga-02-sync-queue-sla-closure.md'),
 (Join-Path $repoRoot 'docs\ga\ga-02-dead-letter-triage-and-historical-evidence.md'),
 (Join-Path $repoRoot 'docs\ga\ga-02-evidence-matrix.md'),
 (Join-Path $repoRoot 'docs\ga\ga-02-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document GA-02 guardrails...'
Assert-True (Test-Path $ga01) 'GA-01 validator missing.'
Assert-True (Test-Path $secretScan) 'Secret scan missing.'
Assert-True (Test-Path $checkSql) 'GA-02 SQL check missing.'
Assert-True (Test-Path $remediationSql) 'GA-02 safe remediation SQL missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required GA-02 document missing: $d"}
Assert-DocumentContains $docs[0] @('GA-02','Sync Queue and SLA Closure','retryPendingCount = 0','retryOverSlaCount = 0','PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03')
Assert-DocumentContains $docs[1] @('GA-02','close_as_historical_evidence','append-only','schemaVersion = 4')
Assert-DocumentContainsAny $docs[1] @('PENDING USER VALIDATION','PASS REAL PRODUCTION')
Assert-DocumentContains $docs[3] @('retry','quarantine','supersede','close_as_historical_evidence','commercial')
Assert-DocumentContains $docs[5] @('PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03','FAIL / HOTFIX REQUIRED')
Assert-DocumentContainsAny $docs[5] @('PENDING USER VALIDATION','PASS REAL PRODUCTION')
Write-Step 'Repository/document GA-02 guardrails PASS'

Write-Step 'Secret scan...'
Unblock-File $secretScan -ErrorAction SilentlyContinue
& $secretScan -Root $repoRoot
Write-Step 'Secret scan PASS'

Write-Step 'Fresh GA-01 prerequisite revalidation...'
Unblock-File $ga01 -ErrorAction SilentlyContinue
& $ga01 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
Assert-True (Test-Path $ga01Manifest) 'Fresh GA-01 manifest missing.'
$g1=Get-Content -Raw $ga01Manifest | ConvertFrom-Json
Assert-True ($g1.status -eq 'PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02') 'Fresh GA-01 prerequisite did not PASS.'
Assert-True (@($g1.blockers).Count -eq 0) 'Fresh GA-01 prerequisite contains blockers.'
Assert-True ([int]$g1.schemaVersion -eq 4) 'Fresh GA-01 schemaVersion drifted from 4.'
Assert-True ([string]$g1.syncContract -eq 'schema_version_4') 'Fresh GA-01 syncContract drifted.'
Assert-True (-not [bool]$g1.generalAvailabilityActivated) 'General Availability is already activated; GA-02 must stop.'
$ga01At=[string]$g1.generatedAt
Write-Step 'Fresh GA-01 prerequisite revalidation PASS'

Write-Step 'GA-02 pre-remediation sync queue diagnostic...'
$before=Invoke-DbJsonFile $checkSql @{tenant_id=$TenantId;ga01_at=$ga01At}
Assert-True ([int]$before.schemaVersion -eq 4) 'GA-02 precheck schemaVersion drifted from 4.'
Assert-True ([string]$before.syncContract -eq 'schema_version_4') 'GA-02 precheck syncContract drifted.'
Assert-True (-not [bool]$before.generalAvailabilityActivated) 'GA-02 precheck indicates GA activated.'
Write-Step ("GA-02 pre-remediation counts: retryPending={0}; retryOverSla={1}; staleProcessing={2}; pendingConflicts={3}; deadLetter={4}; newDeadLetter={5}; untriagedDeadLetter={6}; legacySchema={7}" -f $before.retryPendingCount,$before.retryOverSlaCount,$before.staleProcessingCount,$before.pendingConflictCount,$before.deadLetterCount,$before.newDeadLetterCount,$before.untriagedDeadLetterCount,$before.legacySchemaEventCount)
if(@($before.retryDetails).Count -gt 0){ Write-Host '[GA-02] Retry details:'; $before.retryDetails | ConvertTo-Json -Depth 8 | Write-Host }
if(@($before.deadLetterDetails).Count -gt 0){ Write-Host '[GA-02] Dead-letter details:'; $before.deadLetterDetails | ConvertTo-Json -Depth 8 | Write-Host }
$hardPreBlockers=@($before.blockers | Where-Object { $_ -notin @('retry_without_safe_ga02_decision','dead_letter_requires_explicit_decision') })
Assert-True ($hardPreBlockers.Count -eq 0) "GA-02 hard pre-remediation blockers: $($hardPreBlockers -join ', ')"
Assert-True ([long]$before.ambiguousRetryCount -eq 0) 'GA-02 found retry_pending work that is commercial or ambiguous. Automatic remediation is forbidden; inspect retryDetails.'
Assert-True ([long]$before.actionableOrAmbiguousDeadLetterCount -eq 0) 'GA-02 found a dead-letter that is new, commercial, ambiguous, or lacks a safe historical-evidence classification. Automatic closure is forbidden.'
Write-Step 'GA-02 pre-remediation sync queue diagnostic PASS'

$remediation=$null
if(([long]$before.retryPendingCount -gt 0) -or ([long]$before.historicalValidationDeadLetterCount -gt 0)){
 Write-Step 'Apply append-only-safe GA-02 validation-fixture closure...'
 $remediation=Invoke-DbJsonFile $remediationSql @{tenant_id=$TenantId;ga01_at=$ga01At}
 Write-Step ("GA-02 remediation PASS; closedValidationRetryCount={0}; retryAuditEventCount={1}; historicalDeadLetterDecisionCount={2}; newDeadLetterAuditEventCount={3}" -f $remediation.closedValidationRetryCount,$remediation.retryAuditEventCount,$remediation.historicalDeadLetterDecisionCount,$remediation.newDeadLetterAuditEventCount)
}else{
 Write-Step 'No safe remediation required; sync actionable queue already closed.'
}

Write-Step 'GA-02 post-remediation source-of-truth reconciliation...'
$after=Invoke-DbJsonFile $checkSql @{tenant_id=$TenantId;ga01_at=$ga01At}
$postBlockers=@()
if([long]$after.retryPendingCount -ne 0){$postBlockers+='retry_pending_not_zero'}
if([long]$after.retryOverSlaCount -ne 0){$postBlockers+='retry_over_sla_not_zero'}
if([long]$after.staleProcessingCount -ne 0){$postBlockers+='stale_processing_not_zero'}
if([long]$after.pendingConflictCount -ne 0){$postBlockers+='pending_conflict_not_zero'}
if([long]$after.newDeadLetterCount -ne 0){$postBlockers+='new_dead_letter_not_zero'}
if([long]$after.untriagedDeadLetterCount -ne 0){$postBlockers+='untriaged_dead_letter_not_zero'}
if([long]$after.legacySchemaEventCount -ne 0){$postBlockers+='legacy_schema_event_not_zero'}
if([long]$after.duplicateBatchSequenceViolationCount -ne 0 -or [long]$after.duplicateEventIdentityCount -ne 0){$postBlockers+='idempotency_violation'}
if([long]$after.actionableOrAmbiguousDeadLetterCount -ne 0){$postBlockers+='dead_letter_executable_work_pending'}
if([long]$after.historicalValidationDeadLetterCount -gt 0 -and [long]$after.historicalDeadLetterAuditCount -lt [long]$after.historicalValidationDeadLetterCount){$postBlockers+='historical_dead_letter_audit_evidence_missing'}
Assert-True ($postBlockers.Count -eq 0) "GA-02 post-remediation blockers: $($postBlockers -join ', ')"
Write-Step 'GA-02 post-remediation source-of-truth reconciliation PASS'

$generated=(Get-Date).ToUniversalTime().ToString('o')
$historicalDecision=if([long]$after.deadLetterCount -gt 0){'close_as_historical_evidence'}else{'none'}
$snapshot=[ordered]@{
 phase='GA-02';generatedAt=$generated;tenantId=$TenantId;baseUrl=$base;ga01At=$ga01At;
 before=[ordered]@{retryPendingCount=[long]$before.retryPendingCount;retryDueCount=[long]$before.retryDueCount;retryOverSlaCount=[long]$before.retryOverSlaCount;validationRetryCount=[long]$before.validationRetryCount;ambiguousRetryCount=[long]$before.ambiguousRetryCount;staleProcessingCount=[long]$before.staleProcessingCount;pendingConflictCount=[long]$before.pendingConflictCount;deadLetterCount=[long]$before.deadLetterCount;newDeadLetterCount=[long]$before.newDeadLetterCount;untriagedDeadLetterCount=[long]$before.untriagedDeadLetterCount;historicalValidationDeadLetterCount=[long]$before.historicalValidationDeadLetterCount;legacySchemaEventCount=[long]$before.legacySchemaEventCount;retryDetails=$before.retryDetails;deadLetterDetails=$before.deadLetterDetails};
 remediation=$remediation;
 after=[ordered]@{retryPendingCount=[long]$after.retryPendingCount;retryDueCount=[long]$after.retryDueCount;retryOverSlaCount=[long]$after.retryOverSlaCount;staleProcessingCount=[long]$after.staleProcessingCount;pendingConflictCount=[long]$after.pendingConflictCount;deadLetterCount=[long]$after.deadLetterCount;newDeadLetterCount=[long]$after.newDeadLetterCount;untriagedDeadLetterCount=[long]$after.untriagedDeadLetterCount;actionableOrAmbiguousDeadLetterCount=[long]$after.actionableOrAmbiguousDeadLetterCount;legacySchemaEventCount=[long]$after.legacySchemaEventCount;processedSchema4SyncCount=[long]$after.processedSchema4SyncCount;duplicateBatchSequenceViolationCount=[long]$after.duplicateBatchSequenceViolationCount;duplicateEventIdentityCount=[long]$after.duplicateEventIdentityCount;historicalDeadLetterAuditCount=[long]$after.historicalDeadLetterAuditCount;retryClosureAuditCount=[long]$after.retryClosureAuditCount};
 deadLetterDecision=$historicalDecision;blockers=$postBlockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false
}
$snapshot | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-02 Sync Queue and SLA Closure Evidence

Generated: $generated
Tenant: $TenantId
Fresh GA-01 generatedAt: $ga01At

## Before
- retryPendingCount: $($before.retryPendingCount)
- retryDueCount: $($before.retryDueCount)
- retryOverSlaCount: $($before.retryOverSlaCount)
- staleProcessingCount: $($before.staleProcessingCount)
- pendingConflictCount: $($before.pendingConflictCount)
- deadLetterCount: $($before.deadLetterCount)
- newDeadLetterCount: $($before.newDeadLetterCount)
- untriagedDeadLetterCount: $($before.untriagedDeadLetterCount)
- legacySchemaEventCount: $($before.legacySchemaEventCount)

## Safe decision policy
- commercial or ambiguous retry: never auto-close
- new/untriaged/ambiguous dead-letter: never auto-close
- over-SLA controlled validation retry: close_as_historical_evidence with append-only audit
- triaged pre-GA-01 controlled validation dead-letter: retain row, audit close_as_historical_evidence

## After
- retryPendingCount: $($after.retryPendingCount)
- retryOverSlaCount: $($after.retryOverSlaCount)
- staleProcessingCount: $($after.staleProcessingCount)
- pendingConflictCount: $($after.pendingConflictCount)
- newDeadLetterCount: $($after.newDeadLetterCount)
- untriagedDeadLetterCount: $($after.untriagedDeadLetterCount)
- legacySchemaEventCount: $($after.legacySchemaEventCount)
- deadLetterDecision: $historicalDecision
- blockers: none

## Decision
PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03
General Availability remains NOT activated.
"@ | Set-Content -Encoding UTF8 $evidencePath
$manifest=[ordered]@{
 phase='GA-02';status='PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;entryGate='PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02';ga01At=$ga01At;syncClosureContract='ga_sync_queue_sla_closure';
 retryPendingCount=[long]$after.retryPendingCount;retryDueCount=[long]$after.retryDueCount;retryOverSlaCount=[long]$after.retryOverSlaCount;staleProcessingCount=[long]$after.staleProcessingCount;pendingConflictCount=[long]$after.pendingConflictCount;deadLetterCount=[long]$after.deadLetterCount;newDeadLetterCount=[long]$after.newDeadLetterCount;untriagedDeadLetterCount=[long]$after.untriagedDeadLetterCount;historicalDeadLetterDecision=$historicalDecision;legacySchemaEventCount=[long]$after.legacySchemaEventCount;processedSchema4SyncCount=[long]$after.processedSchema4SyncCount;duplicateBatchSequenceViolationCount=[long]$after.duplicateBatchSequenceViolationCount;duplicateEventIdentityCount=[long]$after.duplicateEventIdentityCount;historicalDeadLetterAuditCount=[long]$after.historicalDeadLetterAuditCount;retryClosureAuditCount=[long]$after.retryClosureAuditCount;closedValidationRetryCount=if($null -eq $remediation){0}else{[long]$remediation.closedValidationRetryCount};historicalDeadLetterAuditDecisionCount=if($null -eq $remediation){0}else{[long]$remediation.historicalDeadLetterDecisionCount};blockers=$postBlockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-03 - Support, Incident and SLO Operations Readiness'
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $manifestPath
@"
# GA-02 Sync Queue and SLA Closure Log

- status: $($manifest.status)
- generatedAt: $generated
- retryPendingCount: $($manifest.retryPendingCount)
- retryOverSlaCount: $($manifest.retryOverSlaCount)
- staleProcessingCount: $($manifest.staleProcessingCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
- deadLetterCount: $($manifest.deadLetterCount)
- historicalDeadLetterDecision: $($manifest.historicalDeadLetterDecision)
- newDeadLetterCount: $($manifest.newDeadLetterCount)
- untriagedDeadLetterCount: $($manifest.untriagedDeadLetterCount)
- legacySchemaEventCount: $($manifest.legacySchemaEventCount)
- blockers: none
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: false
- nextPhase: GA-03 - Support, Incident and SLO Operations Readiness
"@ | Set-Content -Encoding UTF8 $logPath
Write-Step 'GA-02 evidence manifest and closure snapshot PASS'
Write-Step 'GA-02 PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03'
[pscustomobject]$manifest | Format-List
