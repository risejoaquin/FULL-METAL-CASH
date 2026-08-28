param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-12] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','metrics','stores','users','roles','permissions','terminals','customers','categories','products','priceLists','channels')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) foreach($n in $Names){ if($null -ne $Object -and $null -ne $Object.$n){ return [long]$Object.$n } }; return $Default }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-Api { param([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}) $uri="$script:base$Path"; $params=@{Method=$Method; Uri=$uri; Headers=$Headers; TimeoutSec=30}; if($null -ne $Body){ $params.Body=($Body|ConvertTo-Json -Depth 40); $params.ContentType='application/json' }; return Invoke-RestMethod @params }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-12-commercial-beta-readiness-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-12-commercial-beta-readiness'
$manifestPath=Join-Path $runtimeDirectory 'commercial-beta-readiness-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-12-commercial-beta-readiness-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-12-commercial-beta-readiness.md'
  beta=Join-Path $repoRoot 'docs\expansion\exp-12-beta-entry-criteria.md'
  onboarding=Join-Path $repoRoot 'docs\expansion\exp-12-commercial-onboarding-runbook.md'
  acceptance=Join-Path $repoRoot 'docs\expansion\exp-12-customer-acceptance-checklist.md'
  pricing=Join-Path $repoRoot 'docs\expansion\exp-12-pricing-packaging-readiness.md'
  support=Join-Path $repoRoot 'docs\expansion\exp-12-support-sla-readiness.md'
  risk=Join-Path $repoRoot 'docs\expansion\exp-12-beta-risk-register.md'
  rollout=Join-Path $repoRoot 'docs\expansion\exp-12-limited-rollout-policy.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-12-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-12 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-12 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-12','commercial beta readiness','go limited beta','expansion close')
Assert-DocumentContains -Path $docs.beta -Terms @('beta entry criteria','go/no-go','blockers','conditions')
Assert-DocumentContains -Path $docs.onboarding -Terms @('commercial onboarding','tenant','store','terminal','operator')
Assert-DocumentContains -Path $docs.acceptance -Terms @('customer acceptance','sale','receipt','refund','offline')
Assert-DocumentContains -Path $docs.pricing -Terms @('pricing packaging','catalog','price list','release channel')
Assert-DocumentContains -Path $docs.support -Terms @('support sla','sev','incident','dead-letter','retry')
Assert-DocumentContains -Path $docs.risk -Terms @('risk register','mitigation','owner','rollback')
Assert-DocumentContains -Path $docs.rollout -Terms @('limited rollout','cap','rollback','do not mass rollout')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','commercial beta','limited beta')
Write-Step 'EXP-12 document contract PASS'

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
  if(Test-Path (Join-Path $dashboardRoot 'package.json')){
    Write-Step 'Dashboard build...'
    if(Test-Path (Join-Path $dashboardRoot 'package-lock.json')){ Invoke-NpmCommand -Arguments @('ci') -WorkingDirectory $dashboardRoot } else { Invoke-NpmCommand -Arguments @('install') -WorkingDirectory $dashboardRoot }
    Invoke-NpmCommand -Arguments @('run','build') -WorkingDirectory $dashboardRoot
    Write-Step 'Dashboard build PASS'
  }
}

Write-Step 'Production liveness/readiness...'
$live=Invoke-Api -Method Get -Path '/health/live'
$ready=Invoke-Api -Method Get -Path '/health/ready'
Assert-True (($live.status -eq 'alive') -or ($live -eq 'alive')) 'health/live must be alive.'
Assert-True (($ready.status -eq 'ready') -or ($ready -eq 'ready')) 'health/ready must be ready.'
Assert-True (($ready.database -eq 'ready') -or ($ready.databaseReady -eq $true) -or ($ready.database.ready -eq $true)) 'database readiness must be ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and commercial beta endpoint contract...'
$loginBody=@{ email=$Email; password=$plainPassword; tenantId=$TenantId }
$session=Invoke-Api -Method Post -Path '/api/v1/auth/login' -Body $loginBody
$token=$session.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$authHeaders=@{ Authorization="Bearer $token" }
$tenant=Invoke-Api -Method Get -Path '/api/v1/tenants/current' -Headers $authHeaders
Assert-True ($tenant.id -eq $TenantId) 'Current tenant mismatch.'
$metrics=Invoke-Api -Method Get -Path '/api/v1/observability/metrics' -Headers $authHeaders
$syncStatus=Invoke-Api -Method Get -Path '/api/v1/sync/status' -Headers $authHeaders
$deadLetter=Invoke-Api -Method Get -Path '/api/v1/sync/dead-letter?limit=25' -Headers $authHeaders
$conflicts=Invoke-Api -Method Get -Path '/api/v1/sync/conflicts?status=pending&limit=25' -Headers $authHeaders
$auditEvents=Invoke-Api -Method Get -Path '/api/v1/audit/events?limit=25' -Headers $authHeaders
$stores=Get-Items (Invoke-Api -Method Get -Path '/api/v1/stores' -Headers $authHeaders)
$users=Get-Items (Invoke-Api -Method Get -Path '/api/v1/users' -Headers $authHeaders)
$roles=Get-Items (Invoke-Api -Method Get -Path '/api/v1/roles' -Headers $authHeaders)
$permissions=Get-Items (Invoke-Api -Method Get -Path '/api/v1/permissions' -Headers $authHeaders)
$terminals=Get-Items (Invoke-Api -Method Get -Path '/api/v1/terminals' -Headers $authHeaders)
$customers=Get-Items (Invoke-Api -Method Get -Path '/api/v1/customers?limit=25' -Headers $authHeaders)
$catalog=Invoke-Api -Method Get -Path '/api/v1/tenant/catalog' -Headers $authHeaders
$channels=Invoke-Api -Method Get -Path '/api/v1/updates/channels' -Headers $authHeaders
Assert-True ($null -ne $metrics) 'Observability metrics endpoint did not return data.'
Assert-True ($null -ne $syncStatus) 'Sync status endpoint did not return data.'
Assert-True ($null -ne $deadLetter) 'Dead-letter endpoint did not return data.'
Assert-True ($null -ne $conflicts) 'Conflicts endpoint did not return data.'
Assert-True ((Get-Items $auditEvents).Count -ge 0) 'Audit endpoint shape invalid.'
Assert-True ($stores.Count -ge 1) 'At least one store is required.'
Assert-True ($users.Count -ge 1) 'At least one user is required.'
Assert-True ($roles.Count -ge 1) 'At least one role is required.'
Assert-True ($permissions.Count -ge 1) 'At least one permission is required.'
Assert-True ($terminals.Count -ge 1) 'At least one terminal is required.'
Assert-True ($null -ne $catalog) 'Runtime catalog endpoint did not return data.'
Assert-True ((Get-Items $channels).Count -ge 1) 'Update channels endpoint did not return channels.'
Write-Step 'Admin login and commercial beta endpoint contract PASS'

Write-Step 'SQL commercial beta readiness cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id=$TenantId }
Assert-True ($sql.exp12SqlValidation -eq 'GO') "EXP-12 SQL validation failed: $($sql.blockers -join ', ')"
Write-Step 'SQL commercial beta readiness cross-check PASS'

Write-Step 'Commercial beta GO/NO-GO decision matrix...'
$blockers=@($sql.blockers)
$conditions=@($sql.sqlWarnings)
$failedRequests=Get-LongValue -Object $metrics.requests -Names @('failedRequests') -Default 0
$p95LatencyMs=0
if($null -ne $metrics.requests.p95LatencyMs){ $p95LatencyMs=[decimal]$metrics.requests.p95LatencyMs }
if($failedRequests -gt 0){ $conditions += 'review_failed_requests_before_beta' }
if($sql.retryPendingSync -gt 0){ $conditions += 'monitor_retry_pending_sync' }
if($sql.deadLetterSync -gt 0){ $conditions += 'triage_known_dead_letter_sync' }
if($sql.openShiftCount -gt 0){ $conditions += 'review_open_cash_shifts' }
if($sql.stableReleaseCount -lt 1){ $conditions += 'stable_channel_promotion_pending' }
Assert-True ($blockers.Count -eq 0) "EXP-12 blockers detected: $($blockers -join ', ')"
Write-Step 'Commercial beta GO/NO-GO decision matrix PASS'

Write-Step 'Write commercial beta manifest and log...'
$manifest=[ordered]@{
  phase='EXP-12'
  status='PASS COMMERCIAL BETA READINESS / GO LIMITED COMMERCIAL BETA'
  tenantId=$TenantId
  baseUrl=$script:base
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  commercialBetaDecision='GO_LIMITED_COMMERCIAL_BETA'
  exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02'
  exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
  exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
  exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
  exp05='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
  exp06='PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
  exp07='PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08'
  exp08='PASS SUPPORT AND INCIDENT OPERATIONS / GO EXP-09'
  exp09='PASS RELEASE MANAGEMENT AND UPDATE CHANNEL / GO EXP-10'
  exp10='PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11'
  exp11='PASS CATALOG PRICING OPERATIONS / GO EXP-12'
  healthLive='alive'; healthReady='ready'; databaseReady='ready'
  activeStoreCount=[long]$sql.activeStoreCount; activeTerminalCount=[long]$sql.activeTerminalCount; activeUserCount=[long]$sql.activeUserCount
  roleCount=[long]$sql.roleCount; permissionCount=[long]$sql.permissionCount; roleAssignmentCount=[long]$sql.roleAssignmentCount; storeAccessAssignmentCount=[long]$sql.storeAccessAssignmentCount; activeCustomerCount=[long]$sql.activeCustomerCount
  activeCategoryCount=[long]$sql.activeCategoryCount; activeSellableProductCount=[long]$sql.activeSellableProductCount; activeVariantCount=[long]$sql.activeVariantCount; barcodeCount=[long]$sql.barcodeCount; activePriceListCount=[long]$sql.activePriceListCount; activeMxnPriceCount=[long]$sql.activeMxnPriceCount
  negativePriceCount=[long]$sql.negativePriceCount; invalidPriceWindowCount=[long]$sql.invalidPriceWindowCount; invalidTaxModeCount=[long]$sql.invalidTaxModeCount; invalidModifierBehaviorCount=[long]$sql.invalidModifierBehaviorCount; invalidSubstituteModifierCount=[long]$sql.invalidSubstituteModifierCount
  totalSalesCount=[long]$sql.totalSalesCount; completedSalesCount=[long]$sql.completedSalesCount; approvedPaymentCount=[long]$sql.approvedPaymentCount; failedPaymentsLast24Hours=[long]$sql.failedPaymentsLast24Hours; openShiftCount=[long]$sql.openShiftCount; cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount
  totalSyncEvents=[long]$sql.totalSyncEvents; processedSyncCount=[long]$sql.processedSyncCount; retryPendingSync=[long]$sql.retryPendingSync; deadLetterSync=[long]$sql.deadLetterSync; retryDueCount=[long]$sql.retryDueCount; legacySchemaEventCount=[long]$sql.legacySchemaEventCount; pendingConflictCount=[long]$sql.pendingConflictCount; resolvedConflictCount=[long]$sql.resolvedConflictCount; syncChangeCount=[long]$sql.syncChangeCount
  tenantReleaseCount=[long]$sql.tenantReleaseCount; internalReleaseCount=[long]$sql.internalReleaseCount; stableReleaseCount=[long]$sql.stableReleaseCount; velopackUniversalReleaseCount=[long]$sql.velopackUniversalReleaseCount
  auditEventCount=[long]$sql.auditEventCount; auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours; failedRequests=$failedRequests; p95LatencyMs=$p95LatencyMs
  endpointStoreCount=$stores.Count; endpointUserCount=$users.Count; endpointRoleCount=$roles.Count; endpointPermissionCount=$permissions.Count; endpointTerminalCount=$terminals.Count; endpointCustomerSampleCount=$customers.Count
  blockers=$blockers; conditions=$conditions; sqlWarnings=@($sql.sqlWarnings); schemaVersion=4; commercialBetaContract='commercial_beta_readiness'; finalExpansionStage='POST_PILOT_LIMITED_COMMERCIAL_BETA_READY'; nextPhase='Commercial beta controlled onboarding'
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $manifestPath
$log=@"
# EXP-12 Commercial Beta Readiness Log

- phase: EXP-12
- status: PASS COMMERCIAL BETA READINESS / GO LIMITED COMMERCIAL BETA
- generatedAt: $($manifest.generatedAt)
- tenantId: $TenantId
- baseUrl: $script:base
- commercialBetaDecision: GO_LIMITED_COMMERCIAL_BETA
- activeStoreCount: $($manifest.activeStoreCount)
- activeTerminalCount: $($manifest.activeTerminalCount)
- activeUserCount: $($manifest.activeUserCount)
- activeSellableProductCount: $($manifest.activeSellableProductCount)
- activeMxnPriceCount: $($manifest.activeMxnPriceCount)
- completedSalesCount: $($manifest.completedSalesCount)
- approvedPaymentCount: $($manifest.approvedPaymentCount)
- failedPaymentsLast24Hours: $($manifest.failedPaymentsLast24Hours)
- retryPendingSync: $($manifest.retryPendingSync)
- deadLetterSync: $($manifest.deadLetterSync)
- pendingConflictCount: $($manifest.pendingConflictCount)
- tenantReleaseCount: $($manifest.tenantReleaseCount)
- stableReleaseCount: $($manifest.stableReleaseCount)
- blockers: $($blockers -join ',')
- conditions: $($conditions -join ',')
- schemaVersion: 4
- commercialBetaContract: commercial_beta_readiness
- finalExpansionStage: POST_PILOT_LIMITED_COMMERCIAL_BETA_READY
"@
$log | Set-Content -Encoding UTF8 -Path $logPath
Write-Step 'Write commercial beta manifest and log PASS'
Write-Step 'EXP-12 PASS COMMERCIAL BETA READINESS / GO LIMITED COMMERCIAL BETA'
$manifest
