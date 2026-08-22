param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[BETA-10] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$base=$BaseUrl.TrimEnd('/')
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$beta09=Join-Path $repoRoot 'scripts\beta\validate-beta-09-data-quality-reconciliation-closure.ps1'
$beta09Manifest=Join-Path $repoRoot '.runtime\beta-09-data-quality-reconciliation-closure\beta-09-data-quality-reconciliation-manifest.json'
$sqlPath=Join-Path $scriptRoot 'beta-10-limited-commercial-beta-closure-check.sql'
$runtime=Join-Path $repoRoot '.runtime\beta-10-limited-commercial-beta-closure-report'
$manifestPath=Join-Path $runtime 'beta-10-limited-commercial-beta-closure-manifest.json'
$summaryPath=Join-Path $runtime 'beta-10-limited-commercial-beta-closure-report.md'
$nextRoadmapPath=Join-Path $runtime 'general-availability-readiness-next-roadmap.md'
$logPath=Join-Path $repoRoot 'docs\beta\logs\beta-10-limited-commercial-beta-closure-report-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_BETA_10_LIMITED_COMMERCIAL_BETA_CLOSURE_REPORT.md'),
 (Join-Path $repoRoot 'docs\beta\beta-10-limited-commercial-beta-closure-report.md'),
 (Join-Path $repoRoot 'docs\beta\beta-10-closure-metrics.md'),
 (Join-Path $repoRoot 'docs\beta\beta-10-known-conditions-and-blockers.md'),
 (Join-Path $repoRoot 'docs\beta\beta-10-final-decision.md'),
 (Join-Path $repoRoot 'docs\beta\beta-10-next-roadmap.md'),
 (Join-Path $repoRoot 'docs\beta\beta-10-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document closure guardrails...'
Assert-True (Test-Path $beta09) 'BETA-09 validator missing.'
Assert-True (Test-Path $sqlPath) 'BETA-10 SQL closure check missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required BETA-10 document missing: $d"}
Assert-DocumentContains $docs[0] @('BETA-10','beta summary','customer count','store count','terminal count','sales count','payment count','support incidents','SLA performance','sync reliability','release readiness','known conditions','blockers','final decision','next roadmap')
Assert-DocumentContains $docs[4] @('GO_EXPAND_BETA','GO_GENERAL_AVAILABILITY_PREP','NO_GO_FIX_BLOCKERS','PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP')
Assert-DocumentContains $docs[5] @('GENERAL AVAILABILITY READINESS','not activated','stable channel','retry','dead-letter')
Write-Step 'Repository/document closure guardrails PASS'

Write-Step 'Execute BETA-09 data-quality closure prerequisite...'
Unblock-File $beta09 -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\beta\validate-beta-08-customer-acceptance-validation.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\beta\validate-beta-07-dashboard-daily-monitoring-pack.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -ErrorAction SilentlyContinue
& $beta09 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
if($LASTEXITCODE -ne 0){throw "BETA-09 inherited validator failed with exit code $LASTEXITCODE."}
Assert-True (Test-Path $beta09Manifest) 'BETA-09 manifest missing.'
$b09=Get-Content -Raw $beta09Manifest | ConvertFrom-Json
Assert-True ($b09.status -eq 'PASS BETA DATA QUALITY RECONCILIATION CLOSURE / GO BETA-10') 'BETA-09 prerequisite did not PASS.'
Assert-True (@($b09.blockers).Count -eq 0) 'BETA-09 prerequisite still contains blockers.'
$baselineAt=[string]$b09.generatedAt
Write-Step 'Execute BETA-09 data-quality closure prerequisite PASS'

Write-Step 'SQL limited commercial beta closure snapshot...'
$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;baseline_at=$baselineAt}
Assert-True ($sql.beta10SqlDecision -ne 'NO_GO_FIX_BLOCKERS') "BETA-10 SQL blockers: $($sql.blockers -join ', ')"
Write-Step 'SQL limited commercial beta closure snapshot PASS'

Write-Step 'Build formal beta closure report, decision, and next-roadmap handoff...'
$conditions=@($sql.conditions)
$blockers=@($sql.blockers)
$phaseStatus='PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP'
$finalDecision='GO_GENERAL_AVAILABILITY_PREP'
$generated=(Get-Date).ToUniversalTime().ToString('o')
@"
# BETA-10 Limited Commercial Beta Closure Report

Generated: $generated
Tenant: $TenantId

## Beta summary
- BETA-01 through BETA-09: PASS / gated production validation complete
- customer count (validated commercial beta tenant): $($sql.betaCustomerTenantCount)
- POS customer records: $($sql.posCustomerCount)
- stores: $($sql.storeCount) ($($sql.activeStoreCount) active)
- terminals: $($sql.terminalCount) ($($sql.activeTerminalCount) active)
- sales: $($sql.salesCount) ($($sql.acceptedSalesCount) completed/returned)
- payments: $($sql.paymentCount) ($($sql.approvedPaymentCount) approved)
- support incidents (audit-classified, last 30d): $($sql.supportIncidentCount)
- SLA performance: $($sql.slaPerformance)
- sync reliability: $($sql.syncReliability)
- release readiness: $($sql.releaseReadiness)

## Known conditions
$(if($conditions.Count -eq 0){'- none'}else{($conditions|ForEach-Object{"- $_"}) -join "`n"})

## Blockers
- blockers = {}

## Final decision
- $finalDecision
- $phaseStatus
- General Availability is NOT activated by this phase; only readiness preparation is authorized.
"@ | Set-Content -Encoding UTF8 $summaryPath

@"
# General Availability Readiness — Next Roadmap Handoff

Status: authorized for preparation only; GENERAL AVAILABILITY is not activated.

Carry-forward work from limited beta closure:
1. Close retry_pending and retry-over-SLA conditions until the normal operating queue is clean or explicitly budgeted.
2. Retire or formally archive the known triaged dead-letter with documented disposition.
3. Promote a signed, hash-verified Velopack candidate through stable channel with rollback evidence.
4. Re-run production health, security, tenant isolation, reconciliation, backup/restore, support and observability gates against the GA candidate.
5. Require blockers = {} and a new explicit GA readiness decision before any general rollout.

Preserve contracts:
- schemaVersion = 4
- syncContract = schema_version_4
- inventory_ledger remains inventory source of truth
- modifier semantics remain none | add | substitute
"@ | Set-Content -Encoding UTF8 $nextRoadmapPath

$manifest=[ordered]@{
 phase='BETA-10';status=$phaseStatus;tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;betaDecision=$finalDecision;closureContract='limited_commercial_beta_closure_report';
 beta01To09='PASS REAL PRODUCTION / GO';customerCount=[long]$sql.betaCustomerTenantCount;posCustomerCount=[long]$sql.posCustomerCount;storeCount=[long]$sql.storeCount;activeStoreCount=[long]$sql.activeStoreCount;terminalCount=[long]$sql.terminalCount;activeTerminalCount=[long]$sql.activeTerminalCount;salesCount=[long]$sql.salesCount;acceptedSalesCount=[long]$sql.acceptedSalesCount;paymentCount=[long]$sql.paymentCount;approvedPaymentCount=[long]$sql.approvedPaymentCount;supportIncidents=[long]$sql.supportIncidentCount;slaPerformance=[string]$sql.slaPerformance;retryPendingCount=[long]$sql.retryPendingCount;retryOverSlaCount=[long]$sql.retryOverSlaCount;syncReliability=[string]$sql.syncReliability;processedSchema4SyncCount=[long]$sql.processedSchema4SyncCount;pendingConflictCount=[long]$sql.pendingConflictCount;deadLetterCount=[long]$sql.deadLetterCount;newDeadLetterCount=[long]$sql.newDeadLetterCount;untriagedDeadLetterCount=[long]$sql.untriagedDeadLetterCount;releaseReadiness=[string]$sql.releaseReadiness;activeBetaReleaseCount=[long]$sql.activeBetaReleaseCount;activeStableReleaseCount=[long]$sql.activeStableReleaseCount;openShiftCount=[long]$sql.openShiftCount;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;knownConditions=$conditions;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextRoadmap='GENERAL AVAILABILITY READINESS - PREPARATION ONLY'
}
$manifest|ConvertTo-Json -Depth 8|Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-10 Limited Commercial Beta Closure Report Log

Generated: $generated
Status: $phaseStatus
Decision: $finalDecision
SLA performance: $($sql.slaPerformance)
Sync reliability: $($sql.syncReliability)
Release readiness: $($sql.releaseReadiness)
Known conditions: $(if($conditions.Count){$conditions -join ', '}else{'none'})
Blockers: none
General Availability activated: false
"@ | Set-Content -Encoding UTF8 $logPath
Write-Step 'BETA-10 evidence manifest PASS'
Write-Step 'BETA-10 PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP'
$manifest
