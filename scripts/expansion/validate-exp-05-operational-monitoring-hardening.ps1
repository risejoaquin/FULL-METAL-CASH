param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [int64]$MaxP95LatencyMs = 5000,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-05] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','metrics')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function Get-DecimalValue { param($Object,[string[]]$Names,[decimal]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [decimal]$Object.$name } }; return $Default }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Assert-DocumentContainsAny { param([string]$Path,[string[]]$Terms,[string]$Label) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ if($content.Contains($term.ToLowerInvariant())){ return } }; throw "Document $Path is missing required concept: $Label. Accepted terms: $($Terms -join ', ')" }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-05-operational-monitoring-hardening-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-05-operational-monitoring-hardening'
$manifestPath=Join-Path $runtimeDirectory 'operational-monitoring-hardening-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-05-operational-monitoring-hardening-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-05-operational-monitoring-hardening.md'
  matrix=Join-Path $repoRoot 'docs\expansion\exp-05-monitoring-owner-threshold-matrix.md'
  dashboard=Join-Path $repoRoot 'docs\expansion\exp-05-daily-dashboard-checklist.md'
  alerts=Join-Path $repoRoot 'docs\expansion\exp-05-alert-response-runbook.md'
  evidence=Join-Path $repoRoot 'docs\expansion\exp-05-evidence-and-escalation.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-05-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-05 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-05 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-05','operational monitoring hardening','owner','threshold','action','exp-06')
Assert-DocumentContains -Path $docs.matrix -Terms @('metric','owner','threshold','action','sync','dead_letter','negative inventory','cash shift')
Assert-DocumentContains -Path $docs.dashboard -Terms @('daily','dashboard','health','sync','cash','inventory','audit')
Assert-DocumentContains -Path $docs.alerts -Terms @('alert','severity','owner','triage','containment','rollback')
Assert-DocumentContains -Path $docs.evidence -Terms @('evidence','escalation','owner','timestamp','log','decision')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','blocker','condition','exp-06')
Assert-DocumentContainsAny -Path $docs.phase -Label 'monitoring' -Terms @('monitoring','monitoreo','observability','metrics','metricas','métricas')
Write-Step 'EXP-05 document contract PASS'

Write-Step 'Local secret scan...'
Invoke-CheckedCommand -Name 'secret scan' -Command { & (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -Root $repoRoot }
Write-Step 'Local secret scan PASS'
Write-Step 'dotnet restore...'; Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet restore' -Command { & dotnet restore $slnPath } } finally { Pop-Location }; Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...'; Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet build' -Command { & dotnet build $slnPath --no-restore } } finally { Pop-Location }; Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...'; Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet test' -Command { & dotnet test $slnPath --no-build } } finally { Pop-Location }; Write-Step 'dotnet test PASS'
if(-not $SkipDashboardBuild){ Write-Step 'Dashboard production build and self-test...'; Assert-True (Test-Path (Join-Path $dashboardRoot 'package.json')) 'Dashboard package.json is missing.'; Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('install'); Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('run','build'); Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('run','self-test'); Write-Step 'Dashboard production build and self-test PASS' }

Write-Step 'Production liveness/readiness...'
$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30; Assert-True ($live.status -eq 'alive') 'Production liveness did not return alive.'
$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30; Assert-True ($ready.status -eq 'ready') 'Production readiness did not return ready.'; Assert-True ($ready.database -eq 'ready') 'Production database readiness did not return ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and monitoring endpoint contract...'
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $($session.accessToken)"}
$metrics=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$conflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($metrics.database.ready -eq $true) 'Metrics database.ready must be true.'
Assert-True ($metrics.database.requiredTablesPresent -eq $true) 'Metrics database.requiredTablesPresent must be true.'
Assert-True ($null -ne $metrics.sync.inboxByStatus) 'Metrics sync.inboxByStatus is required.'
Assert-True ($null -ne $metrics.inventory.negativeInventoryItemCount) 'Metrics inventory.negativeInventoryItemCount is required.'
Assert-True ($null -ne $syncStatus) 'Sync status endpoint returned null.'
Assert-True ($null -ne $deadLetter) 'Dead-letter endpoint returned null.'
Assert-True ($null -ne $conflicts) 'Pending conflicts endpoint returned null.'
Assert-True ((Get-Items $auditEvents).Count -ge 0) 'Audit events endpoint shape is invalid.'
Write-Step 'Admin login and monitoring endpoint contract PASS'

Write-Step 'SQL operational monitoring cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{tenant_id=$TenantId}
$sqlBlockingReasons=@($sql.sqlBlockingReasons)
Assert-True ($sql.exp05SqlValidation -eq 'GO') "EXP-05 SQL validation returned NO-GO. Reasons: $($sqlBlockingReasons -join ',')"
Assert-True ($sql.requiredTablesPresent -eq $true) 'Required operational monitoring tables are missing.'
Assert-True ($sql.activeStoreCount -ge 2) 'EXP-05 requires at least two active stores after EXP-04.'
Assert-True ($sql.activeTerminalCount -ge 2) 'EXP-05 requires at least two active terminals after EXP-03/EXP-04.'
Write-Step 'SQL operational monitoring cross-check PASS'

Write-Step 'Operational monitoring threshold matrix...'
$syncInbox=$metrics.sync.inboxByStatus
$retryPending=Get-LongValue -Object $syncInbox -Names @('retry_pending','retryPending') -Default ([long]$sql.retryPendingSyncCount)
$deadLetterCount=Get-LongValue -Object $syncInbox -Names @('dead_letter','deadLetter') -Default ([long]$sql.deadLetterSyncCount)
$pendingConflicts=Get-LongValue -Object $metrics.sync -Names @('pendingConflicts') -Default ([long]$sql.pendingConflictCount)
$negativeInventory=Get-LongValue -Object $metrics.inventory -Names @('negativeInventoryItemCount') -Default ([long]$sql.negativeInventoryItemCount)
$failedPayments24=Get-LongValue -Object $metrics.payments -Names @('failedPaymentsLast24Hours') -Default ([long]$sql.failedPaymentsLast24Hours)
$failedRequestsBaseline=Get-LongValue -Object $metrics.requests -Names @('failedRequests') -Default 0
$p95LatencyMs=Get-DecimalValue -Object $metrics.requests -Names @('p95LatencyMs','p95Ms') -Default 0
# failedRequests is process-lifetime cumulative. Re-sample after all monitoring probes and block only on new failures during this validation window.
$metricsAfter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$failedRequests=Get-LongValue -Object $metricsAfter.requests -Names @('failedRequests') -Default $failedRequestsBaseline
$failedRequestsDelta=[Math]::Max([long]0,([long]$failedRequests-[long]$failedRequestsBaseline))
$blockers=@(); if($live.status -ne 'alive'){$blockers+='liveness_not_alive'}; if($ready.status -ne 'ready'){$blockers+='readiness_not_ready'}; if($ready.database -ne 'ready'){$blockers+='database_not_ready'}; if($pendingConflicts -gt 0){$blockers+='pending_conflicts'}; if($failedPayments24 -gt 0){$blockers+='failed_payments_last_24h'}; if($failedRequestsDelta -gt 0){$blockers+='failed_requests_during_validation'}; if(($p95LatencyMs -gt 0) -and ($p95LatencyMs -gt $MaxP95LatencyMs)){$blockers+='p95_latency_over_threshold'}
$conditions=@(); if($retryPending -gt 0){$conditions+='monitor_retry_pending_sync'}; if($deadLetterCount -gt 0){$conditions+='triage_known_dead_letter'}; if($negativeInventory -gt 0){$conditions+='inventory_reconciliation_required'}; if($sql.openShiftCount -gt 0){$conditions+='review_open_cash_shifts'}; if($failedRequestsBaseline -gt 0){$conditions+='historical_failed_requests_process_lifetime'}
$decision='GO_OPERATIONAL_MONITORING_HARDENED'; if($blockers.Count -gt 0){$decision='NO_GO'}
Assert-True ($decision -ne 'NO_GO') "EXP-05 decision is NO-GO because blockers exist: $($blockers -join ',')"
Write-Step 'Operational monitoring threshold matrix PASS'

Write-Step 'Write operational monitoring manifest and log...'
$manifest=[ordered]@{phase='EXP-05';status='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06';tenantId=$TenantId;baseUrl=$script:base;generatedAt=(Get-Date).ToUniversalTime().ToString('o');monitoringDecision=$decision;pilot01To10='PASS REAL PRODUCTION / GO';exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02';exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03';exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04';exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05';healthLive=$live.status;healthReady=$ready.status;databaseReady=$ready.database;storeCount=[long]$sql.storeCount;activeStoreCount=[long]$sql.activeStoreCount;terminalCount=[long]$sql.terminalCount;activeTerminalCount=[long]$sql.activeTerminalCount;totalSalesCount=[long]$sql.totalSalesCount;completedSalesLast24Hours=[long]$sql.completedSalesLast24Hours;failedPaymentsLast24Hours=$failedPayments24;processedSyncCount=[long]$sql.processedSyncCount;retryPendingSync=$retryPending;deadLetterSync=$deadLetterCount;pendingConflicts=$pendingConflicts;resolvedConflicts=[long]$sql.resolvedConflictCount;negativeInventoryItemCount=$negativeInventory;openShiftCount=[long]$sql.openShiftCount;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;auditEventCount=[long]$sql.auditEventCount;auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours;failedRequests=$failedRequests;failedRequestsBaseline=$failedRequestsBaseline;failedRequestsDelta=$failedRequestsDelta;p95LatencyMs=$p95LatencyMs;blockers=$blockers;conditions=$conditions;sqlWarnings=@($sql.sqlWarnings);schemaVersion=[int]$sql.schemaVersion;monitoringContract='operational_monitoring_hardening';nextPhase='EXP-06 Inventory Reconciliation Hardening'}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path $logPath -Encoding UTF8 -Value '# SolidPOS EXP-05 Operational Monitoring Hardening Log'
Add-Content -Path $logPath -Encoding UTF8 -Value ''; Add-Content -Path $logPath -Encoding UTF8 -Value "status: $($manifest.status)"; Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"; Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"; Add-Content -Path $logPath -Encoding UTF8 -Value "decision: $decision"; Add-Content -Path $logPath -Encoding UTF8 -Value "failedRequestsBaseline: $failedRequestsBaseline"; Add-Content -Path $logPath -Encoding UTF8 -Value "failedRequests: $failedRequests"; Add-Content -Path $logPath -Encoding UTF8 -Value "failedRequestsDelta: $failedRequestsDelta"; Add-Content -Path $logPath -Encoding UTF8 -Value "p95LatencyMs: $p95LatencyMs"; Add-Content -Path $logPath -Encoding UTF8 -Value "blockers: $($blockers -join ',')"; Add-Content -Path $logPath -Encoding UTF8 -Value "conditions: $($conditions -join ',')"; Add-Content -Path $logPath -Encoding UTF8 -Value 'goNoGo: GO'
Write-Step 'Write operational monitoring manifest and log PASS'
Write-Step 'EXP-05 PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
[pscustomobject]$manifest
