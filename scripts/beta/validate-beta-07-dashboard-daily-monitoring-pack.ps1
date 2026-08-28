param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[BETA-07] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){ throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)} finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)} }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; $global:LASTEXITCODE=0; return ($json|ConvertFrom-Json) }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Invoke-Npm { param([string[]]$Args,[string]$Dir) Push-Location $Dir; try{$global:LASTEXITCODE=0; & npm @Args; if($LASTEXITCODE -ne 0){throw "npm $($Args -join ' ') failed."}} finally{Pop-Location}; $global:LASTEXITCODE=0 }

$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$exp05=Join-Path $repoRoot 'scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1'
$exp05Manifest=Join-Path $repoRoot '.runtime\exp-05-operational-monitoring-hardening\operational-monitoring-hardening-manifest.json'
$sqlPath=Join-Path $scriptRoot 'beta-07-dashboard-daily-monitoring-pack-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtime=Join-Path $repoRoot '.runtime\beta-07-dashboard-daily-monitoring-pack'
$manifestPath=Join-Path $runtime 'beta-07-dashboard-daily-monitoring-manifest.json'
$snapshotPath=Join-Path $runtime 'beta-07-daily-monitoring-snapshot.json'
$packPath=Join-Path $runtime 'beta-07-daily-monitoring-pack.md'
$logPath=Join-Path $repoRoot 'docs\beta\logs\beta-07-dashboard-daily-monitoring-pack-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_BETA_07_BETA_DASHBOARD_AND_DAILY_MONITORING_PACK.md')
 (Join-Path $repoRoot 'docs\beta\beta-07-dashboard-daily-monitoring-pack.md')
 (Join-Path $repoRoot 'docs\beta\beta-07-daily-monitoring-checklist.md')
 (Join-Path $repoRoot 'docs\beta\beta-07-monitoring-thresholds-and-owners.md')
 (Join-Path $repoRoot 'docs\beta\beta-07-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document/dashboard source guardrails...'
Assert-True (Test-Path $exp05) 'EXP-05 monitoring validator missing.'
Assert-True (Test-Path $sqlPath) 'BETA-07 SQL snapshot missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($doc in $docs){Assert-True (Test-Path $doc) "Required BETA-07 document missing: $doc"}
Assert-DocumentContains $docs[0] @('BETA-07','dashboard','daily monitoring','GO BETA-08')
Assert-DocumentContains $docs[2] @('health','sync','dead-letter','conflict','cash','sales','inventory','audit')
Assert-DocumentContains $docs[3] @('blocker','condition','owner','threshold','support')
$client=Get-Content -Raw (Join-Path $dashboardRoot 'src\api\posServerClient.ts')
$ops=Get-Content -Raw (Join-Path $dashboardRoot 'src\features\dashboard\OperationsDashboard.tsx')
Assert-True ($client.Contains('/api/v1/observability/metrics')) 'Dashboard client metrics endpoint missing.'
Assert-True ($client.Contains('getOperationalMetrics')) 'Dashboard getOperationalMetrics missing.'
foreach($label in @('Database monitor','API monitor','Conflict monitor','Inventory risk')){Assert-True ($ops.Contains($label)) "OperationsDashboard missing: $label"}
Write-Step 'Repository/document/dashboard source guardrails PASS'

Write-Step 'Execute hardened operational monitoring contract...'
Unblock-File $exp05 -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -ErrorAction SilentlyContinue
& $exp05 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
if($LASTEXITCODE -ne 0){throw "EXP-05 inherited validator failed with exit code $LASTEXITCODE."}
Assert-True (Test-Path $exp05Manifest) 'EXP-05 manifest missing.'
$monitor=Get-Content -Raw $exp05Manifest | ConvertFrom-Json
Assert-True ($monitor.status -eq 'PASS OPERATIONAL MONITORING HARDENING / GO EXP-06') 'EXP-05 monitoring contract did not PASS.'
Write-Step 'Execute hardened operational monitoring contract PASS'

if(-not $SkipDashboardBuild){
 Write-Step 'PosDashboard production build and self-test...'
 Invoke-Npm -Args @('install') -Dir $dashboardRoot
 Invoke-Npm -Args @('run','build') -Dir $dashboardRoot
 Invoke-Npm -Args @('run','self-test') -Dir $dashboardRoot
 Write-Step 'PosDashboard production build and self-test PASS'
}else{Write-Step 'PosDashboard build skipped by explicit switch; source contract remains validated.'}

Write-Step 'Production daily monitoring endpoint snapshot...'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$h=@{Authorization="Bearer $($session.accessToken)"}
$live=Invoke-RestMethod -Method Get -Uri "$base/health/live" -TimeoutSec 30
$ready=Invoke-RestMethod -Method Get -Uri "$base/health/ready" -TimeoutSec 30
$metrics=Invoke-RestMethod -Method Get -Uri "$base/api/v1/observability/metrics" -Headers $h -TimeoutSec 30
$sync=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/status" -Headers $h -TimeoutSec 30
$dead=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/dead-letter?limit=25" -Headers $h -TimeoutSec 30
$conflicts=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $h -TimeoutSec 30
$audit=Invoke-RestMethod -Method Get -Uri "$base/api/v1/audit/events?limit=25" -Headers $h -TimeoutSec 30
$sales=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sales?limit=25" -Headers $h -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Liveness is not alive.'
Assert-True ($ready.status -eq 'ready' -and $ready.database -eq 'ready') 'Readiness/database is not ready.'
Assert-True ($metrics.database.ready -eq $true) 'Operational metrics database.ready is false.'
Assert-True ($null -ne $sync -and $null -ne $dead -and $null -ne $conflicts -and $null -ne $audit -and $null -ne $sales) 'Daily dependency endpoint returned null.'
Write-Step 'Production daily monitoring endpoint snapshot PASS'

Write-Step 'SQL daily monitoring source-of-truth...'
$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId}
Assert-True ($sql.beta07SqlDecision -eq 'GO') "BETA-07 SQL blockers: $($sql.blockers -join ', ')"
Write-Step 'SQL daily monitoring source-of-truth PASS'

Write-Step 'Build daily monitoring pack and evidence manifest...'
$conditions=@($sql.conditions)
$blockers=@($sql.blockers)
$snapshot=[ordered]@{
 generatedAt=(Get-Date).ToUniversalTime().ToString('o'); tenantId=$TenantId; baseUrl=$base;
 healthLive=$live.status; healthReady=$ready.status; databaseReady=$ready.database;
 totalRequests=$metrics.requests.totalRequests; failedRequests=$metrics.requests.failedRequests; p95LatencyMs=$metrics.requests.p95LatencyMs;
 processedSyncCount=[long]$sql.processedSyncCount; retryPendingSync=[long]$sql.retryPendingSync; retryDueCount=[long]$sql.retryDueCount; retryOverSlaCount=[long]$sql.retryOverSlaCount; deadLetterSync=[long]$sql.deadLetterSync; staleProcessingCount=[long]$sql.staleProcessingCount;
 pendingConflictCount=[long]$sql.pendingConflictCount; resolvedConflictCount=[long]$sql.resolvedConflictCount;
 completedSalesLast24Hours=[long]$sql.completedSalesLast24Hours; failedPaymentsLast24Hours=[long]$sql.failedPaymentsLast24Hours;
 openShiftCount=[long]$sql.openShiftCount; cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;
 negativeInventoryItemCount=[long]$sql.negativeInventoryItemCount; lowStockItemCount=[long]$sql.lowStockItemCount; auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours;
 legacySchemaEventCount=[long]$sql.legacySchemaEventCount; conditions=$conditions; blockers=$blockers; schemaVersion=4; syncContract='schema_version_4'
}
$snapshot | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $snapshotPath
$conditionText=if($conditions.Count -eq 0){'none'}else{$conditions -join ', '}
@"
# BETA-07 Daily Monitoring Pack

Generated: $($snapshot.generatedAt)
Tenant: $TenantId

## Decision
- blockers: $($blockers.Count)
- conditions: $conditionText

## Health and API
- live: $($snapshot.healthLive)
- ready: $($snapshot.healthReady)
- database: $($snapshot.databaseReady)
- failedRequests: $($snapshot.failedRequests)
- p95LatencyMs: $($snapshot.p95LatencyMs)

## Sync
- processed: $($snapshot.processedSyncCount)
- retry pending: $($snapshot.retryPendingSync)
- retry due: $($snapshot.retryDueCount)
- retry over SLA: $($snapshot.retryOverSlaCount)
- dead-letter: $($snapshot.deadLetterSync)
- stale processing: $($snapshot.staleProcessingCount)
- pending conflicts: $($snapshot.pendingConflictCount)

## Store operations
- completed sales 24h: $($snapshot.completedSalesLast24Hours)
- failed payments 24h: $($snapshot.failedPaymentsLast24Hours)
- open shifts: $($snapshot.openShiftCount)
- cash differences 24h: $($snapshot.cashDifferenceLast24HoursCount)

## Inventory and audit
- negative inventory items: $($snapshot.negativeInventoryItemCount)
- low stock items: $($snapshot.lowStockItemCount)
- audit events 24h: $($snapshot.auditEventsLast24Hours)

## Contract
- schemaVersion: 4
- syncContract: schema_version_4
"@ | Set-Content -Encoding UTF8 $packPath
$manifest=[ordered]@{phase='BETA-07';status='PASS BETA DASHBOARD DAILY MONITORING PACK / GO BETA-08';tenantId=$TenantId;baseUrl=$base;generatedAt=$snapshot.generatedAt;betaDecision='GO_BETA_08';dashboardSourceContract='PASS';dashboardBuild=$(if($SkipDashboardBuild){'SKIPPED_BY_SWITCH'}else{'PASS'});dailyMonitoringPack='PASS';monitoringContract='beta_dashboard_daily_monitoring_pack';healthLive=$snapshot.healthLive;healthReady=$snapshot.healthReady;databaseReady=$snapshot.databaseReady;processedSyncCount=$snapshot.processedSyncCount;retryPendingSync=$snapshot.retryPendingSync;retryDueCount=$snapshot.retryDueCount;retryOverSlaCount=$snapshot.retryOverSlaCount;deadLetterSync=$snapshot.deadLetterSync;pendingConflictCount=$snapshot.pendingConflictCount;completedSalesLast24Hours=$snapshot.completedSalesLast24Hours;failedPaymentsLast24Hours=$snapshot.failedPaymentsLast24Hours;openShiftCount=$snapshot.openShiftCount;cashDifferenceLast24HoursCount=$snapshot.cashDifferenceLast24HoursCount;negativeInventoryItemCount=$snapshot.negativeInventoryItemCount;lowStockItemCount=$snapshot.lowStockItemCount;auditEventsLast24Hours=$snapshot.auditEventsLast24Hours;blockers=$blockers;conditions=$conditions;schemaVersion=4;syncContract='schema_version_4';nextPhase='BETA-08 - Beta Customer Acceptance Validation'}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-07 Dashboard and Daily Monitoring Pack Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- dashboardSourceContract: PASS
- dashboardBuild: $($manifest.dashboardBuild)
- blockers: $($blockers -join ', ')
- conditions: $conditionText
- schemaVersion: 4
- syncContract: schema_version_4
"@ | Set-Content -Encoding UTF8 $logPath
Assert-True ($blockers.Count -eq 0) "BETA-07 blockers: $($blockers -join ', ')"
Write-Step 'BETA-07 evidence manifest PASS'
Write-Step 'BETA-07 PASS BETA DASHBOARD DAILY MONITORING PACK / GO BETA-08'
[pscustomobject]$manifest | Format-List
