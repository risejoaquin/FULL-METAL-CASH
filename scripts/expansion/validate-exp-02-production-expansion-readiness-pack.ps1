param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-02] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Assert-DocumentContainsAny { param([string]$Path,[string[]]$Terms,[string]$Label) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ if($content.Contains($term.ToLowerInvariant())){ return } }; throw "Document $Path is missing required concept: $Label. Accepted terms: $($Terms -join ', ')" }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-02-production-expansion-readiness-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-02-production-expansion-readiness-pack'
$manifestPath=Join-Path $runtimeDirectory 'expansion-readiness-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-02-production-expansion-readiness-pack-log.md'
$docs=@{ phase=Join-Path $repoRoot 'docs\expansion\exp-02-production-expansion-readiness-pack.md'; storeChecklist=Join-Path $repoRoot 'docs\expansion\exp-02-store-expansion-checklist.md'; terminalChecklist=Join-Path $repoRoot 'docs\expansion\exp-02-terminal-expansion-checklist.md'; onboardingRunbook=Join-Path $repoRoot 'docs\expansion\exp-02-onboarding-runbook.md'; storeRollback=Join-Path $repoRoot 'docs\expansion\exp-02-store-rollback-runbook.md'; dailyGoNoGo=Join-Path $repoRoot 'docs\expansion\exp-02-daily-go-no-go.md'; monitoringMatrix=Join-Path $repoRoot 'docs\expansion\exp-02-post-expansion-monitoring-matrix.md'; acceptancePolicy=Join-Path $repoRoot 'docs\expansion\exp-02-new-store-terminal-acceptance-policy.md'; operatorChecklist=Join-Path $repoRoot 'docs\expansion\exp-02-operator-checklist.md'; goNoGo=Join-Path $repoRoot 'docs\expansion\exp-02-go-no-go.md' }
Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-02 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
Write-Step 'Local repository guardrails PASS'
Write-Step 'Expansion readiness document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-02','production expansion readiness pack','go_limited_expansion','exp-03')
Assert-DocumentContains -Path $docs.storeChecklist -Terms @('store','pre-expansion','catalog','inventory','rollback','go/no-go')
Assert-DocumentContains -Path $docs.terminalChecklist -Terms @('terminal','enrollment','bootstrap sync','cash shift','audit','go/no-go')
Assert-DocumentContains -Path $docs.onboardingRunbook -Terms @('onboarding','owner','operator','terminal','evidence','rollback')
Assert-DocumentContains -Path $docs.storeRollback -Terms @('rollback','store','containment','decision','evidence','recovery')
Assert-DocumentContains -Path $docs.dailyGoNoGo -Terms @('daily','go','no-go','readiness','approval')
Assert-DocumentContainsAny -Path $docs.dailyGoNoGo -Label 'monitoring' -Terms @('monitoring','monitoreo','monitored','observability','metrics','metricas','métricas')
Assert-DocumentContains -Path $docs.monitoringMatrix -Terms @('metric','owner','threshold','action','retry_pending','dead_letter','negative inventory')
Assert-DocumentContains -Path $docs.acceptancePolicy -Terms @('acceptance','new store','new terminal','criteria','pilot','limited expansion')
Assert-DocumentContains -Path $docs.operatorChecklist -Terms @('before','during','after','evidence','rollback')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','blocker','condition','exp-03')
Assert-DocumentContainsAny -Path $docs.phase -Label 'limited expansion' -Terms @('go_limited_expansion','limited expansion','expansion limitada')
Write-Step 'Expansion readiness document contract PASS'
Write-Step 'Local secret scan...'
Invoke-CheckedCommand -Name 'secret scan' -Command { & (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -Root $repoRoot }
Write-Step 'Local secret scan PASS'
Write-Step 'dotnet restore...'
Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet restore' -Command { & dotnet restore $slnPath } } finally { Pop-Location }
Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...'
Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet build' -Command { & dotnet build $slnPath --no-restore } } finally { Pop-Location }
Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...'
Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet test' -Command { & dotnet test $slnPath --no-build } } finally { Pop-Location }
Write-Step 'dotnet test PASS'
if(-not $SkipDashboardBuild){ Write-Step 'Dashboard production build and self-test...'; Assert-True (Test-Path (Join-Path $dashboardRoot 'package.json')) 'Dashboard package.json is missing.'; Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('install'); Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('run','build'); Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('run','self-test'); Write-Step 'Dashboard production build and self-test PASS' }
Write-Step 'Production liveness/readiness...'
$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30; Assert-True ($live.status -eq 'alive') 'Production liveness did not return alive.'
$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30; Assert-True ($ready.status -eq 'ready') 'Production readiness did not return ready.'; Assert-True ($ready.database -eq 'ready') 'Production database readiness did not return ready.'
Write-Step 'Production liveness/readiness PASS'
Write-Step 'Admin login and expansion monitoring endpoints...'
$loginBody=@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $($session.accessToken)"}
$metrics=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$conflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $metrics.database.ready) 'Metrics database.ready is required.'; Assert-True ($null -ne $metrics.sync.inboxByStatus) 'Metrics sync.inboxByStatus is required.'; Assert-True ($null -ne $metrics.inventory.negativeInventoryItemCount) 'Metrics inventory.negativeInventoryItemCount is required.'; Assert-True ($null -ne $syncStatus) 'Sync status returned null.'; Assert-True ($null -ne $deadLetter) 'Dead-letter endpoint returned null.'; Assert-True ($null -ne $conflicts) 'Conflicts endpoint returned null.'; Assert-True ((Get-Items $auditEvents).Count -ge 0) 'Audit events endpoint shape is invalid.'
Write-Step 'Admin login and expansion monitoring endpoints PASS'
Write-Step 'SQL expansion readiness cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{tenant_id=$TenantId}
$sqlBlockingReasons=@($sql.sqlBlockingReasons)
Assert-True ($sql.exp02SqlValidation -eq 'GO') "EXP-02 SQL validation returned NO-GO. Reasons: $($sqlBlockingReasons -join ',')"
Assert-True ($sql.tenantExists -eq $true) 'Tenant must exist in production DB.'; Assert-True ($sql.requiredTablesPresent -eq $true) 'Required production expansion tables are missing.'
Write-Step 'SQL expansion readiness cross-check PASS'
Write-Step 'Readiness decision matrix...'
$syncInbox=$metrics.sync.inboxByStatus
$retryPending=Get-LongValue -Object $syncInbox -Names @('retry_pending','retryPending') -Default ([long]$sql.retryPendingSyncCount)
$deadLetterCount=Get-LongValue -Object $syncInbox -Names @('dead_letter','deadLetter') -Default ([long]$sql.deadLetterSyncCount)
$pendingConflicts=Get-LongValue -Object $metrics.sync -Names @('pendingConflicts') -Default ([long]$sql.pendingConflictCount)
$negativeInventory=Get-LongValue -Object $metrics.inventory -Names @('negativeInventoryItemCount') -Default 0
$failedPayments24=Get-LongValue -Object $metrics.payments -Names @('failedPaymentsLast24Hours') -Default ([long]$sql.failedPaymentsLast24Hours)
$failedRequests=Get-LongValue -Object $metrics.requests -Names @('failedRequests') -Default 0
$blockers=@(); if($live.status -ne 'alive'){$blockers+='liveness_not_alive'}; if($ready.status -ne 'ready'){$blockers+='readiness_not_ready'}; if($ready.database -ne 'ready'){$blockers+='database_not_ready'}; if($pendingConflicts -gt 0){$blockers+='pending_conflicts'}; if($failedPayments24 -gt 0){$blockers+='failed_payments_last_24h'}; if($sql.storeCount -lt 1){$blockers+='store_missing'}; if($sql.terminalCount -lt 1){$blockers+='terminal_missing'}
$conditions=@(); if($retryPending -gt 0){$conditions+='monitor_retry_pending_sync'}; if($deadLetterCount -gt 0){$conditions+='triage_known_dead_letter'}; if($negativeInventory -gt 0){$conditions+='inventory_reconciliation_required'}; if($failedRequests -gt 0){$conditions+='review_failed_requests'}
$decision='GO_EXPANSION_READINESS_PACK'; if($blockers.Count -gt 0){$decision='NO_GO'}; Assert-True ($decision -ne 'NO_GO') "EXP-02 decision is NO-GO because blockers exist: $($blockers -join ',')"
Write-Step 'Readiness decision matrix PASS'
Write-Step 'Write readiness manifest and log...'
$manifest=[ordered]@{phase='EXP-02';status='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03';tenantId=$TenantId;baseUrl=$script:base;generatedAt=(Get-Date).ToUniversalTime().ToString('o');readinessDecision=$decision;pilot01To10='PASS REAL PRODUCTION / GO';exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02';productionExpansionDecision='GO_LIMITED_EXPANSION';healthLive=$live.status;healthReady=$ready.status;databaseReady=$ready.database;storeCount=$sql.storeCount;terminalCount=$sql.terminalCount;userCount=$sql.userCount;totalSalesCount=$sql.totalSalesCount;approvedPaymentCount=$sql.approvedPaymentCount;failedPaymentsLast24Hours=$failedPayments24;processedSyncCount=$sql.processedSyncCount;retryPendingSync=$retryPending;deadLetterSync=$deadLetterCount;pendingConflicts=$pendingConflicts;resolvedConflicts=$sql.resolvedConflictCount;negativeInventoryItemCount=$negativeInventory;auditEventCount=$sql.auditEventCount;failedRequests=$failedRequests;blockers=$blockers;conditions=$conditions;sqlWarnings=@($sql.sqlWarnings);schemaVersion=$sql.schemaVersion;readinessContract='production_expansion_readiness_pack';nextPhase='EXP-03 Second Terminal Production Expansion'}
$manifest|ConvertTo-Json -Depth 8|Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path $logPath -Encoding UTF8 -Value '# SolidPOS EXP-02 Production Expansion Readiness Pack Log'
Add-Content -Path $logPath -Encoding UTF8 -Value ''; Add-Content -Path $logPath -Encoding UTF8 -Value 'status: PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'; Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"; Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"; Add-Content -Path $logPath -Encoding UTF8 -Value "decision: $decision"; Add-Content -Path $logPath -Encoding UTF8 -Value "blockers: $($blockers -join ',')"; Add-Content -Path $logPath -Encoding UTF8 -Value "conditions: $($conditions -join ',')"; Add-Content -Path $logPath -Encoding UTF8 -Value 'goNoGo: GO'
Write-Step 'Write readiness manifest and log PASS'
Write-Step 'EXP-02 PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
[pscustomobject]$manifest
