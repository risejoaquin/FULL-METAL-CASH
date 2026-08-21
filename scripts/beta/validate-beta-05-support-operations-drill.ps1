param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step([string]$m){ Write-Host "[BETA-05] $m" }
function Assert-True([bool]$c,[string]$m){ if(-not $c){ throw $m } }
function Invoke-CheckedCommand([string]$Name,[scriptblock]$Command){ $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-DbJsonFile([string]$SqlPath,[hashtable]$Variables){
    $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $fileName=Split-Path -Leaf $SqlPath
    $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1')
    foreach($key in $Variables.Keys){ $args += @('-v',"$key=$($Variables[$key])") }
    $args += @('-f',"/sql/$fileName")
    $global:LASTEXITCODE=0
    $out=docker @args
    if($LASTEXITCODE -ne 0){ throw "DB JSON file command failed for $SqlPath." }
    $global:LASTEXITCODE=0
    $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1)
    Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'
    return ($json|ConvertFrom-Json)
}
function Assert-Document([string]$Path,[string[]]$Terms){
    Assert-True (Test-Path $Path) "Required document missing: $Path"
    $content=(Get-Content -Raw $Path).ToLowerInvariant()
    foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path missing required term: $term" }
}

$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sqlPath=Join-Path $scriptRoot 'beta-05-support-operations-drill-check.sql'
$exp08=Join-Path $repoRoot 'scripts\expansion\validate-exp-08-support-incident-operations.ps1'
$runtime=Join-Path $repoRoot '.runtime\beta-05-support-operations-drill'
$manifestPath=Join-Path $runtime 'beta-05-support-operations-manifest.json'
$evidencePath=Join-Path $runtime 'beta-05-support-evidence-package.json'
$logPath=Join-Path $repoRoot 'docs\beta\logs\beta-05-support-operations-drill-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\beta\beta-05-support-operations-drill.md'
  intake=Join-Path $repoRoot 'docs\beta\beta-05-incident-intake-and-resolution-checklist.md'
  communication=Join-Path $repoRoot 'docs\beta\beta-05-customer-communication-template.md'
  goNoGo=Join-Path $repoRoot 'docs\beta\beta-05-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document guardrails...'
Assert-True (Test-Path $sqlPath) 'BETA-05 SQL validator missing.'
Assert-True (Test-Path $exp08) 'EXP-08 support validator missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Assert-Document $docs.phase @('beta-05','support operations drill','sev','retry_pending','dead-letter','rollback','beta-06')
Assert-Document $docs.intake @('incident intake','sev','evidence','retry','rollback','resolution')
Assert-Document $docs.communication @('customer','incident','status','next update')
Assert-Document $docs.goNoGo @('go','no-go','blockers','beta-06')
Write-Step 'Repository/document guardrails PASS'

Write-Step 'Unblock inherited support validator...'
Unblock-File $exp08 -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -ErrorAction SilentlyContinue
Write-Step 'Unblock inherited support validator PASS'

Write-Step 'Execute hardened support/incident operations contract...'
if($SkipDashboardBuild){
    & $exp08 -BaseUrl $BaseUrl -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild
}else{
    & $exp08 -BaseUrl $BaseUrl -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl
}
if($LASTEXITCODE -ne 0){ throw "EXP-08 support validator failed with exit code $LASTEXITCODE." }
Write-Step 'Execute hardened support/incident operations contract PASS'

Write-Step 'BETA-05 SQL operational triage snapshot...'
$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId}
Assert-True ($sql.beta05SqlDecision -eq 'GO') "BETA-05 SQL blockers: $($sql.blockers -join ', ')"
Write-Step 'BETA-05 SQL operational triage snapshot PASS'

Write-Step 'Build support evidence package and decision paths...'
$retryDecision = if([long]$sql.retryDueCount -gt 0){ 'MANUAL_RETRY_OR_WORKER_DECISION_REQUIRED' } elseif([long]$sql.retryPendingSync -gt 0){ 'MONITOR_NOT_DUE' } else { 'NO_RETRY_ACTION_REQUIRED' }
$deadLetterDecision = if([long]$sql.deadLetterSync -gt 0){ 'TRIAGE_AND_QUARANTINE_OR_CORRECT_THEN_RETRY' } else { 'NO_DEAD_LETTER_ACTION_REQUIRED' }
$openShiftDecision = if([long]$sql.openShiftCount -gt 0){ 'DAILY_REVIEW_REQUIRED_DO_NOT_FORCE_CLOSE_WITHOUT_OPERATOR_EVIDENCE' } else { 'NO_OPEN_SHIFT_ACTION_REQUIRED' }
$rollbackDecision = if(([long]$sql.pendingConflictCount -gt 0) -or ([long]$sql.staleProcessingCount -gt 0)){ 'ESCALATE_AND_EVALUATE_ROLLBACK' } else { 'ROLLBACK_NOT_TRIGGERED_DRILL_PATH_VALIDATED' }
$sev = if(([long]$sql.pendingConflictCount -gt 0) -or ([long]$sql.staleProcessingCount -gt 0)){ 'SEV2' } elseif(([long]$sql.deadLetterSync -gt 0) -or ([long]$sql.retryDueCount -gt 0)){ 'SEV3' } else { 'SEV4_MONITORING' }
$conditions=@($sql.conditions)
$blockers=@($sql.blockers)
$evidence=[ordered]@{
  phase='BETA-05'; generatedAt=(Get-Date).ToUniversalTime().ToString('o'); tenantId=$TenantId; severity=$sev
  incidentIntakeStatus='CAPTURED'; supportEvidenceStatus='COMPLETE'; retryDecision=$retryDecision
  deadLetterDecision=$deadLetterDecision; openShiftDecision=$openShiftDecision; rollbackDecision=$rollbackDecision
  retryPendingSync=[long]$sql.retryPendingSync; retryDueCount=[long]$sql.retryDueCount
  deadLetterSync=[long]$sql.deadLetterSync; deadLetterWithEvidenceCount=[long]$sql.deadLetterWithEvidenceCount
  openShiftCount=[long]$sql.openShiftCount; pendingConflictCount=[long]$sql.pendingConflictCount
  auditEventCount=[long]$sql.auditEventCount; auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours
  conditions=$conditions; blockers=$blockers
  evidenceSources=@('EXP-08 support contract','observability/sync/audit protected endpoints','PostgreSQL source-of-truth','beta incident intake checklist','rollback runbook')
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $evidencePath
Write-Step 'Build support evidence package and decision paths PASS'

Write-Step 'Build BETA-05 decision manifest...'
$status='PASS BETA SUPPORT OPERATIONS DRILL / GO BETA-06'
$manifest=[ordered]@{
  phase='BETA-05'; status=$status; tenantId=$TenantId; baseUrl=$BaseUrl.TrimEnd('/'); generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  betaDecision='GO_BETA_06'; supportDrillContract='beta_support_operations_drill'; severity=$sev
  incidentIntake='PASS'; supportRunbook='PASS'; incidentEvidence='PASS'; auditEvidence='PASS'; monitoringMetrics='PASS'
  manualRetryDecisionPath=$retryDecision; deadLetterTriagePath=$deadLetterDecision; openShiftReviewPath=$openShiftDecision; rollbackDecisionPath=$rollbackDecision
  retryPendingSync=[long]$sql.retryPendingSync; retryDueCount=[long]$sql.retryDueCount; deadLetterSync=[long]$sql.deadLetterSync
  openShiftCount=[long]$sql.openShiftCount; pendingConflictCount=[long]$sql.pendingConflictCount
  auditEventCount=[long]$sql.auditEventCount; auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours
  blockers=$blockers; conditions=$conditions; schemaVersion=4; syncContract='schema_version_4'; nextPhase='BETA-06 - Beta Release Promotion and Rollback Drill'
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-05 Support Operations Drill Log

- status: $status
- generatedAt: $($manifest.generatedAt)
- severity: $sev
- retryDecision: $retryDecision
- deadLetterDecision: $deadLetterDecision
- openShiftDecision: $openShiftDecision
- rollbackDecision: $rollbackDecision
- retryPendingSync: $($sql.retryPendingSync)
- retryDueCount: $($sql.retryDueCount)
- deadLetterSync: $($sql.deadLetterSync)
- openShiftCount: $($sql.openShiftCount)
- pendingConflictCount: $($sql.pendingConflictCount)
- blockers: $($blockers -join ', ')
- conditions: $($conditions -join ', ')
- schemaVersion: 4
- syncContract: schema_version_4
"@ | Set-Content -Encoding UTF8 $logPath
Write-Step 'BETA-05 evidence manifest PASS'
Write-Step $status
[pscustomobject]$manifest
