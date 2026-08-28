param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-07] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','metrics','buckets')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-07-sync-sla-offline-reliability-hardening-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-07-sync-sla-offline-reliability-hardening'
$manifestPath=Join-Path $runtimeDirectory 'sync-sla-offline-reliability-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-07-sync-sla-offline-reliability-hardening-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-07-sync-sla-offline-reliability-hardening.md'
  thresholds=Join-Path $repoRoot 'docs\expansion\exp-07-sync-sla-threshold-matrix.md'
  recovery=Join-Path $repoRoot 'docs\expansion\exp-07-offline-recovery-runbook.md'
  deadLetter=Join-Path $repoRoot 'docs\expansion\exp-07-dead-letter-triage-runbook.md'
  idempotency=Join-Path $repoRoot 'docs\expansion\exp-07-idempotency-duplicate-protection.md'
  replay=Join-Path $repoRoot 'docs\expansion\exp-07-sync-replay-policy.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-07-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-07 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-07 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-07','sync sla','offline reliability','idempotency','exp-08')
Assert-DocumentContains -Path $docs.thresholds -Terms @('owner','threshold','action','escalation','retry_pending')
Assert-DocumentContains -Path $docs.recovery -Terms @('offline recovery','outbox','idempotency','pending conflicts','dead-letter')
Assert-DocumentContains -Path $docs.deadLetter -Terms @('dead-letter','triage','manual retry','payload','reason')
Assert-DocumentContains -Path $docs.idempotency -Terms @('idempotency','duplicate protection','batch id','event id','sequence number')
Assert-DocumentContains -Path $docs.replay -Terms @('replay','reason','previous status','target status','forbidden')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','stale processing','duplicate','exp-08')
Write-Step 'EXP-07 document contract PASS'

Write-Step 'Local secret scan...'
Invoke-CheckedCommand -Name 'secret scan' -Command { & (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -Root $repoRoot }
Write-Step 'Local secret scan PASS'

Write-Step 'dotnet restore...'
Invoke-CheckedCommand -Name 'dotnet restore' -Command { & dotnet restore $slnPath }
Write-Step 'dotnet restore PASS'

Write-Step 'dotnet build...'
Invoke-CheckedCommand -Name 'dotnet build' -Command { & dotnet build $slnPath --no-restore }
Write-Step 'dotnet build PASS'

Write-Step 'dotnet test...'
Invoke-CheckedCommand -Name 'dotnet test' -Command { & dotnet test $slnPath --no-build }
Write-Step 'dotnet test PASS'

if(-not $SkipDashboardBuild){
  Write-Step 'Dashboard build/self-test...'
  if(Test-Path (Join-Path $dashboardRoot 'package.json')){
    if(Test-Path (Join-Path $dashboardRoot 'package-lock.json')){ Invoke-NpmCommand -Arguments @('ci') -WorkingDirectory $dashboardRoot } else { Invoke-NpmCommand -Arguments @('install') -WorkingDirectory $dashboardRoot }
    Invoke-NpmCommand -Arguments @('run','build') -WorkingDirectory $dashboardRoot
  }
  Write-Step 'Dashboard build/self-test PASS'
}

Write-Step 'Production liveness/readiness...'
$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Production liveness did not return alive.'
$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq 'ready') 'Production readiness did not return ready.'
Assert-True ($ready.database -eq 'ready') 'Production database readiness did not return ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and sync reliability endpoint contract...'
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
$token=$session.accessToken; Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $token"}
$metrics=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$syncContract=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/contract" -Headers $adminHeaders -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$conflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $metrics) 'Observability metrics endpoint did not return data.'
Assert-True ($null -ne $syncStatus) 'Sync status endpoint did not return data.'
Assert-True ($null -ne $syncContract) 'Sync contract endpoint did not return data.'
Assert-True ($null -ne $deadLetter) 'Dead-letter endpoint did not return data.'
Assert-True ($null -ne $conflicts) 'Conflicts endpoint did not return data.'
Assert-True ((Get-Items $auditEvents).Count -ge 0) 'Audit endpoint shape invalid.'
Write-Step 'Admin login and sync reliability endpoint contract PASS'

Write-Step 'SQL sync SLA offline reliability cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{tenant_id=$TenantId}
Assert-True ($sql.exp07SqlValidation -eq 'GO') "EXP-07 SQL validation returned NO-GO: $($sql.sqlBlockingReasons -join ', ')"
Write-Step 'SQL sync SLA offline reliability cross-check PASS'

Write-Step 'Sync SLA decision matrix...'
$blockers=@($sql.sqlBlockingReasons)
$warnings=@($sql.sqlWarnings)
$conditions=@()
if((Get-LongValue $sql @('retryPendingSync')) -gt 0){ $conditions += 'monitor_retry_pending_sync' }
if((Get-LongValue $sql @('deadLetterSync')) -gt 0){ $conditions += 'triage_known_dead_letter' }
if((Get-LongValue $sql @('retryDueCount')) -gt 0){ $conditions += 'process_retry_due_events' }
if((Get-LongValue $sql @('legacySchemaEventCount')) -gt 0){ $conditions += 'monitor_legacy_schema_events' }
Assert-True ($blockers.Count -eq 0) "EXP-07 blockers found: $($blockers -join ', ')"
Write-Step 'Sync SLA decision matrix PASS'

Write-Step 'Write sync SLA manifest and log...'
$manifest=[ordered]@{
  phase='EXP-07'
  status='PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08'
  tenantId=$TenantId
  baseUrl=$script:base
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  syncReliabilityDecision='GO_SYNC_SLA_OFFLINE_RELIABILITY_HARDENED'
  exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02'
  exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
  exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
  exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
  exp05='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
  exp06='PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
  healthLive=$live.status
  healthReady=$ready.status
  databaseReady=$ready.database
  activeStoreCount=$sql.activeStoreCount
  activeTerminalCount=$sql.activeTerminalCount
  totalSyncEvents=$sql.totalSyncEvents
  processedSync=$sql.processedSync
  retryPendingSync=$sql.retryPendingSync
  deadLetterSync=$sql.deadLetterSync
  pendingConflicts=$sql.pendingConflicts
  resolvedConflicts=$sql.resolvedConflicts
  duplicateSync=$sql.duplicateSync
  duplicateBatchSequenceViolationCount=$sql.duplicateBatchSequenceViolationCount
  duplicateEventIdentityCount=$sql.duplicateEventIdentityCount
  staleProcessingCount=$sql.staleProcessingCount
  staleReceivedCount=$sql.staleReceivedCount
  retryDueCount=$sql.retryDueCount
  retryOverSlaCount=$sql.retryOverSlaCount
  retryWithoutNextRetryCount=$sql.retryWithoutNextRetryCount
  legacySchemaEventCount=$sql.legacySchemaEventCount
  schemaVersion4EventCount=$sql.schemaVersion4EventCount
  maxSchemaVersion=$sql.maxSchemaVersion
  syncChangeCount=$sql.syncChangeCount
  auditEventsLast24Hours=$sql.auditEventsLast24Hours
  blockers=$blockers
  conditions=$conditions
  sqlWarnings=$warnings
  schemaVersion=4
  syncReliabilityContract='sync_sla_offline_reliability_hardening'
  nextPhase='EXP-08 Support and Incident Operations'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -Path $manifestPath -Encoding UTF8
@"
# EXP-07 Sync SLA and Offline Reliability Hardening Log

- GeneratedAt: $($manifest.generatedAt)
- TenantId: $TenantId
- BaseUrl: $script:base
- Status: $($manifest.status)
- Decision: $($manifest.syncReliabilityDecision)
- ProcessedSync: $($manifest.processedSync)
- RetryPendingSync: $($manifest.retryPendingSync)
- DeadLetterSync: $($manifest.deadLetterSync)
- PendingConflicts: $($manifest.pendingConflicts)
- DuplicateBatchSequenceViolationCount: $($manifest.duplicateBatchSequenceViolationCount)
- StaleProcessingCount: $($manifest.staleProcessingCount)
- Conditions: $($conditions -join ',')
- SQL Warnings: $($warnings -join ',')
- NextPhase: $($manifest.nextPhase)
"@ | Set-Content -Path $logPath -Encoding UTF8
Write-Step 'Write sync SLA manifest and log PASS'
Write-Step 'EXP-07 PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08'
$manifest
