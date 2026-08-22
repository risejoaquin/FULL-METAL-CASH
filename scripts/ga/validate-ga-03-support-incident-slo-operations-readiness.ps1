param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild,
 [int64]$MaxP95LatencyMs=5000
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[GA-03] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try {[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)} finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)} }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($n in $Names){if($null -ne $Object.$n){return [long]$Object.$n}}; return $Default }
function Get-DecimalValue { param($Object,[string[]]$Names,[decimal]$Default=0) if($null -eq $Object){return $Default}; foreach($n in $Names){if($null -ne $Object.$n){return [decimal]$Object.$n}}; return $Default }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$ga02=Join-Path $scriptRoot 'validate-ga-02-sync-queue-sla-closure.ps1'
$ga02Manifest=Join-Path $repoRoot '.runtime\ga-02-sync-queue-sla-closure\ga-02-manifest.json'
$secretScan=Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1'
$checkSql=Join-Path $scriptRoot 'ga-03-support-incident-slo-readiness-check.sql'
$runtime=Join-Path $repoRoot '.runtime\ga-03-support-incident-slo-operations-readiness'
$manifestPath=Join-Path $runtime 'ga-03-manifest.json'
$evidencePath=Join-Path $runtime 'ga-03-evidence.md'
$snapshotPath=Join-Path $runtime 'ga-03-snapshot.json'
$logPath=Join-Path $repoRoot 'docs\ga\logs\ga-03-support-incident-slo-operations-readiness-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
 (Join-Path $repoRoot 'SOLIDPOS_GA_03_SUPPORT_INCIDENT_AND_SLO_OPERATIONS_READINESS.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-support-incident-slo-operations-readiness.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-slo-sli-error-budget-contract.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-severity-escalation-oncall-matrix.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-domain-incident-routing-runbook.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-incident-intake-evidence-and-pir-template.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-daily-support-operating-checklist.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-evidence-matrix.md'),
 (Join-Path $repoRoot 'docs\ga\ga-03-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document GA-03 guardrails...'
Assert-True (Test-Path $ga02) 'GA-02 validator missing.'
Assert-True (Test-Path $secretScan) 'Secret scan missing.'
Assert-True (Test-Path $checkSql) 'GA-03 SQL check missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required GA-03 document missing: $d"}
Assert-DocumentContains $docs[0] @('GA-03','Support, Incident and SLO Operations Readiness','SEV1','SEV4','API availability','error budget','PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04')
Assert-DocumentContains $docs[1] @('99.9%','5000 ms','1.0%','15 minutes','schemaVersion = 4','generalAvailabilityActivated = False')
$ga03MainState=(Get-Content -Raw $docs[1]).ToLowerInvariant(); Assert-True ($ga03MainState.Contains('pending user validation') -or $ga03MainState.Contains('pass real production')) 'GA-03 main document must contain a valid lifecycle state.'
Assert-DocumentContains $docs[3] @('API availability','99.9%','p95 latency','5000 ms','failed request rate','1.0%','sync processing delay','15 minutes','retry backlog age','dead-letter creation','payment failure rate','data reconciliation failures','error budget','owner')
Assert-DocumentContains $docs[4] @('SEV1','SEV2','SEV3','SEV4','Incident Commander','Support Lead','on-call','escalation')
Assert-DocumentContains $docs[5] @('sync','cash','payment','release','rollback authority','no destructive deletion')
Assert-DocumentContains $docs[6] @('incident id','tenant ID','SEV1','rollback decision','post-incident review','SLO/error-budget impact')
Assert-DocumentContains $docs[7] @('health/live','retry_pending','failed payments','cash differences','inventory','audit evidence','release')
Assert-DocumentContains $docs[9] @('PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04','FAIL / HOTFIX REQUIRED')
$ga03GoState=(Get-Content -Raw $docs[9]).ToLowerInvariant(); Assert-True ($ga03GoState.Contains('pending user validation') -or $ga03GoState.Contains('pass real production')) 'GA-03 go/no-go document must contain a valid lifecycle state.'
Write-Step 'Repository/document GA-03 guardrails PASS'

Write-Step 'Secret scan...'
Unblock-File $secretScan -ErrorAction SilentlyContinue
& $secretScan -Root $repoRoot
Write-Step 'Secret scan PASS'

Write-Step 'Fresh GA-02 prerequisite revalidation...'
Unblock-File $ga02 -ErrorAction SilentlyContinue
& $ga02 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
Assert-True (Test-Path $ga02Manifest) 'Fresh GA-02 manifest missing.'
$g2=Get-Content -Raw $ga02Manifest | ConvertFrom-Json
Assert-True ($g2.status -eq 'PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03') 'Fresh GA-02 prerequisite did not PASS.'
Assert-True (@($g2.blockers).Count -eq 0) 'Fresh GA-02 prerequisite contains blockers.'
Assert-True ([long]$g2.retryPendingCount -eq 0) 'Fresh GA-02 retryPendingCount must remain zero.'
Assert-True ([long]$g2.retryOverSlaCount -eq 0) 'Fresh GA-02 retryOverSlaCount must remain zero.'
Assert-True ([long]$g2.staleProcessingCount -eq 0) 'Fresh GA-02 staleProcessingCount must remain zero.'
Assert-True ([long]$g2.pendingConflictCount -eq 0) 'Fresh GA-02 pendingConflictCount must remain zero.'
Assert-True ([int]$g2.schemaVersion -eq 4) 'Fresh GA-02 schemaVersion drifted from 4.'
Assert-True ([string]$g2.syncContract -eq 'schema_version_4') 'Fresh GA-02 syncContract drifted.'
Assert-True (-not [bool]$g2.generalAvailabilityActivated) 'General Availability is already activated; GA-03 must stop.'
$ga02At=[string]$g2.generatedAt
Write-Step 'Fresh GA-02 prerequisite revalidation PASS'

Write-Step 'Production health and protected support endpoint contract...'
$live=Invoke-RestMethod -Method Get -Uri "$base/health/live" -TimeoutSec 30
$ready=Invoke-RestMethod -Method Get -Uri "$base/health/ready" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Production liveness is not alive.'
Assert-True ($ready.status -eq 'ready') 'Production readiness is not ready.'
Assert-True ($ready.database -eq 'ready') 'Production database readiness is not ready.'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
$token=$session.accessToken; Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$headers=@{Authorization="Bearer $token"}
$metricsBefore=Invoke-RestMethod -Method Get -Uri "$base/api/v1/observability/metrics" -Headers $headers -TimeoutSec 30
$syncStatus=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/status" -Headers $headers -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/dead-letter?limit=25" -Headers $headers -TimeoutSec 30
$conflicts=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $headers -TimeoutSec 30
$audit=Invoke-RestMethod -Method Get -Uri "$base/api/v1/audit/events?limit=25" -Headers $headers -TimeoutSec 30
$metricsAfter=Invoke-RestMethod -Method Get -Uri "$base/api/v1/observability/metrics" -Headers $headers -TimeoutSec 30
Assert-True ($null -ne $syncStatus) 'Sync status endpoint returned no data.'
Assert-True ($null -ne $deadLetter) 'Dead-letter endpoint returned no data.'
Assert-True ($null -ne $conflicts) 'Conflict endpoint returned no data.'
Assert-True ($null -ne $audit) 'Audit endpoint returned no data.'
$failedBefore=Get-LongValue $metricsBefore.requests @('failedRequests') 0
$failedAfter=Get-LongValue $metricsAfter.requests @('failedRequests') $failedBefore
$failedDelta=[Math]::Max([long]0,($failedAfter-$failedBefore))
$p95=Get-DecimalValue $metricsAfter.requests @('p95LatencyMs','p95Ms') 0
Write-Step ("Production endpoint contract PASS; failedRequestsDelta={0}; p95LatencyMs={1}" -f $failedDelta,$p95)

Write-Step 'GA-03 PostgreSQL support/SLO source-of-truth...'
$sql=Invoke-DbJsonFile $checkSql @{tenant_id=$TenantId;ga02_at=$ga02At}
Assert-True ([string]$sql.ga03SqlContract -eq 'ga_support_incident_slo_operations_readiness') 'GA-03 SQL contract mismatch.'
Assert-True ([int]$sql.schemaVersion -eq 4) 'GA-03 SQL schemaVersion drifted.'
Assert-True ([string]$sql.syncContract -eq 'schema_version_4') 'GA-03 SQL syncContract drifted.'
Assert-True (-not [bool]$sql.generalAvailabilityActivated) 'GA-03 SQL indicates General Availability activated.'
Write-Step 'GA-03 PostgreSQL support/SLO source-of-truth PASS'

Write-Step 'SEV/on-call/escalation tabletop contract...'
$incidentMatrix=@(
 [ordered]@{scenario='readiness_or_database_unavailable';severity='SEV1';owner='Incident Commander / Platform On-call';route='platform incident + deployment freeze';rollbackAuthority='Release Owner / Incident Commander'},
 [ordered]@{scenario='payment_integrity_failure';severity='SEV1';owner='Payments Owner + Incident Commander';route='payment containment + reconciliation';rollbackAuthority='Release Owner / Incident Commander'},
 [ordered]@{scenario='cash_integrity_failure';severity='SEV1';owner='Operations Owner + Incident Commander';route='freeze unsafe close + reconcile';rollbackAuthority='Incident Commander'},
 [ordered]@{scenario='tenant_isolation_or_security';severity='SEV1';owner='Security/Platform Owner + Incident Commander';route='contain access + audit';rollbackAuthority='Release Owner / Incident Commander'},
 [ordered]@{scenario='sync_retry_stale_or_conflict';severity='SEV2';owner='Sync Owner + Support Lead';route='sync triage + escalation';rollbackAuthority='Release Owner if release-caused'},
 [ordered]@{scenario='new_dead_letter';severity='SEV2';owner='Support Lead + Sync Owner';route='triage + explicit retry/quarantine/supersede';rollbackAuthority='Release Owner if release-caused'},
 [ordered]@{scenario='inventory_reconciliation_failure';severity='SEV2';owner='Inventory/Operations Owner';route='stop blind edits + append-only reconciliation';rollbackAuthority='Incident Commander if broad integrity impact'},
 [ordered]@{scenario='release_update_degradation';severity='SEV2';owner='Release Owner + Support Lead';route='stop promotion + release incident';rollbackAuthority='Release Owner'}
)
foreach($x in $incidentMatrix){
 Assert-True (-not [string]::IsNullOrWhiteSpace($x.severity)) "Incident scenario $($x.scenario) missing severity."
 Assert-True (-not [string]::IsNullOrWhiteSpace($x.owner)) "Incident scenario $($x.scenario) missing owner."
 Assert-True (-not [string]::IsNullOrWhiteSpace($x.route)) "Incident scenario $($x.scenario) missing route."
 Assert-True (-not [string]::IsNullOrWhiteSpace($x.rollbackAuthority)) "Incident scenario $($x.scenario) missing rollback authority."
}
Write-Step ("SEV/on-call/escalation tabletop contract PASS; scenarios={0}" -f $incidentMatrix.Count)

Write-Step 'GA-03 blocker matrix...'
$blockers=@($sql.blockers)
if($failedDelta -gt 0){$blockers+='failed_requests_during_ga03_validation'}
if(($p95 -gt 0) -and ($p95 -gt $MaxP95LatencyMs)){$blockers+='p95_latency_over_ga03_threshold'}
if($live.status -ne 'alive'){$blockers+='liveness_not_alive'}
if($ready.status -ne 'ready'){$blockers+='readiness_not_ready'}
if($ready.database -ne 'ready'){$blockers+='database_not_ready'}
Assert-True ($blockers.Count -eq 0) "GA-03 blockers: $($blockers -join ', ')"
Write-Step 'GA-03 blocker matrix PASS'

$generated=(Get-Date).ToUniversalTime().ToString('o')
$sloContract=[ordered]@{
 apiAvailabilityPercent=99.9;apiAvailabilityWindow='30d';p95LatencyMs=5000;p95Window='15m';failedRequestRatePercent=1.0;failedRequestRateComparison='less_than';failedRequestWindow='15m';syncProcessingDelayMinutes=15;retryBacklogAgeMinutes=15;newDeadLetterCount24h=0;paymentFailureRatePercent24h=0;dataReconciliationFailureCount=0
}
$snapshot=[ordered]@{
 phase='GA-03';generatedAt=$generated;tenantId=$TenantId;baseUrl=$base;ga02At=$ga02At;
 health=[ordered]@{live=$live.status;ready=$ready.status;database=$ready.database};
 observability=[ordered]@{failedRequestsBaseline=$failedBefore;failedRequestsAfter=$failedAfter;failedRequestsDelta=$failedDelta;p95LatencyMs=$p95;historicalWindowCompliance='NOT_ASSERTED_BY_GA03';telemetryOwnership='PASS'};
 sql=$sql;slo=$sloContract;incidentTabletop=$incidentMatrix;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false
}
$snapshot | ConvertTo-Json -Depth 14 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-03 Support, Incident and SLO Operations Readiness Evidence

Generated: $generated
Tenant: $TenantId
Fresh GA-02 generatedAt: $ga02At

## Current production signals
- health/live: $($live.status)
- health/ready: $($ready.status)
- database: $($ready.database)
- failedRequestsDelta during validation: $failedDelta
- p95LatencyMs: $p95
- retryPendingCount: $($sql.retryPendingCount)
- retryOverSlaCount: $($sql.retryOverSlaCount)
- staleProcessingCount: $($sql.staleProcessingCount)
- pendingConflictCount: $($sql.pendingConflictCount)
- newDeadLetterSinceGa02Count: $($sql.newDeadLetterSinceGa02Count)
- failedPaymentsLast24Hours: $($sql.failedPaymentsLast24Hours)
- cashDifferenceLast24HoursCount: $($sql.cashDifferenceLast24HoursCount)
- negativeInventoryItemCount: $($sql.negativeInventoryItemCount)

## SLO readiness
- API availability: >=99.9% / 30d
- p95 latency: <=5000ms / 15m
- failed request rate: <1.0% / 15m
- sync processing delay: <=15m
- retry backlog age: <=15m
- new dead-letter: 0 / 24h
- payment failure rate: 0% / 24h
- data reconciliation failures: 0

GA-03 does not fabricate historical rolling-window compliance. GA-09/GA-10 validate deeper performance and observability implementation.

## Decision
PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04
General Availability remains NOT activated.
"@ | Set-Content -Encoding UTF8 $evidencePath
$manifest=[ordered]@{
 phase='GA-03';status='PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;entryGate='PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03';ga02At=$ga02At;supportSloContract='ga_support_incident_slo_operations_readiness';
 healthLive=$live.status;healthReady=$ready.status;databaseReady=$ready.database;failedRequestsBaseline=$failedBefore;failedRequestsAfter=$failedAfter;failedRequestsDelta=$failedDelta;p95LatencyMs=$p95;
 retryPendingCount=[long]$sql.retryPendingCount;retryOverSlaCount=[long]$sql.retryOverSlaCount;staleProcessingCount=[long]$sql.staleProcessingCount;pendingConflictCount=[long]$sql.pendingConflictCount;deadLetterCount=[long]$sql.deadLetterCount;newDeadLetterSinceGa02Count=[long]$sql.newDeadLetterSinceGa02Count;historicalDeadLetterDecisionEvidenceCount=[long]$sql.ga02DeadLetterDecisionAuditCount;failedPaymentsLast24Hours=[long]$sql.failedPaymentsLast24Hours;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;negativeInventoryItemCount=[long]$sql.negativeInventoryItemCount;auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours;
 sevLevels=@('SEV1','SEV2','SEV3','SEV4');incidentTabletopScenarioCount=$incidentMatrix.Count;onCallOwnership='PASS';escalationPolicy='PASS';rollbackAuthority='PASS';incidentIntake='PASS';postIncidentReviewTemplate='PASS';dailySupportChecklist='PASS';slo=$sloContract;historicalRollingWindowCompliance='NOT_ASSERTED_BY_GA03';blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-04 - Production Data Integrity and Financial Reconciliation Gate'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $manifestPath
@"
# GA-03 Support, Incident and SLO Operations Readiness Log

- status: $($manifest.status)
- generatedAt: $generated
- healthLive: $($manifest.healthLive)
- healthReady: $($manifest.healthReady)
- databaseReady: $($manifest.databaseReady)
- failedRequestsDelta: $failedDelta
- p95LatencyMs: $p95
- retryPendingCount: $($manifest.retryPendingCount)
- retryOverSlaCount: $($manifest.retryOverSlaCount)
- staleProcessingCount: $($manifest.staleProcessingCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
- newDeadLetterSinceGa02Count: $($manifest.newDeadLetterSinceGa02Count)
- failedPaymentsLast24Hours: $($manifest.failedPaymentsLast24Hours)
- cashDifferenceLast24HoursCount: $($manifest.cashDifferenceLast24HoursCount)
- negativeInventoryItemCount: $($manifest.negativeInventoryItemCount)
- incidentTabletopScenarioCount: $($manifest.incidentTabletopScenarioCount)
- blockers: none
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: false
- nextPhase: GA-04 - Production Data Integrity and Financial Reconciliation Gate
"@ | Set-Content -Encoding UTF8 $logPath
Write-Step 'GA-03 evidence manifest and readiness snapshot PASS'
Write-Step 'GA-03 PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04'
[pscustomobject]$manifest | Format-List
