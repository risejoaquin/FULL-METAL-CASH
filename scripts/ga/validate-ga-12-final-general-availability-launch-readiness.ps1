param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [switch]$SkipDashboardBuild,
    [switch]$SkipGa11Revalidation
)

$ErrorActionPreference = 'Stop'
$script:Ga12ValidatorVersion = 'GA-12.3-sync-inbox-created-at-schema-compatibility'
function Write-Step([string]$message) { Write-Host "[GA-12] $message" }
function Assert-True($condition, [string]$message) {
    $ok = $false
    if ($null -eq $condition) { $ok = $false }
    elseif ($condition -is [bool]) { $ok = $condition }
    elseif ($condition -is [string]) { $ok = -not [string]::IsNullOrWhiteSpace($condition) -and $condition -notin @('False','false','0') }
    elseif ($condition -is [int] -or $condition -is [long] -or $condition -is [decimal] -or $condition -is [double]) { $ok = ([decimal]$condition -ne 0) }
    elseif ($condition -is [System.Array]) { $ok = ($condition.Count -gt 0) }
    else { $ok = $true }
    if (-not $ok) { throw $message }
}
function Convert-Secret([securestring]$secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Invoke-Checked([string]$name, [scriptblock]$command) {
    $global:LASTEXITCODE = 0
    & $command
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode" }
}
function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [hashtable]$Headers = @{}, [int]$TimeoutSec = 30) {
    $params = @{ Method = $Method; Uri = "$script:base$Path"; Headers = $Headers; TimeoutSec = $TimeoutSec }
    if ($null -ne $Body) {
        $params.Body = $Body | ConvertTo-Json -Depth 30
        $params.ContentType = 'application/json'
    }
    try { return Invoke-RestMethod @params }
    catch {
        $status = ''
        $responseText = ''
        if ($_.Exception.Response) {
            try { $status = "; httpStatus=$([int]$_.Exception.Response.StatusCode)" } catch {}
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = New-Object IO.StreamReader($stream)
                    $responseText = $reader.ReadToEnd()
                    $reader.Close()
                }
            } catch {}
        }
        if (-not [string]::IsNullOrWhiteSpace($responseText)) { $status = "$status; response=$responseText" }
        throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"
    }
}
function Get-HttpStatus([string]$Method, [string]$UriOrPath, [hashtable]$Headers = @{}, $Body = $null, [int]$TimeoutSec = 30, [switch]$Absolute) {
    $uri = if ($Absolute.IsPresent) { $UriOrPath } else { "$script:base$UriOrPath" }
    try {
        $params = @{ Method = $Method; Uri = $uri; Headers = $Headers; TimeoutSec = $TimeoutSec; UseBasicParsing = $true }
        if ($null -ne $Body) {
            $params.Body = $Body | ConvertTo-Json -Depth 30
            $params.ContentType = 'application/json'
        }
        $response = Invoke-WebRequest @params
        return [int]$response.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        throw
    }
}
function Invoke-DbJsonFile([string]$SqlPath, [hashtable]$Vars) {
    $directory = (Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $file = Split-Path -Leaf $SqlPath
    $args = @('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${directory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
    foreach ($key in $Vars.Keys) { $args += @('-v', "$key=$($Vars[$key])") }
    $args += @('-f', "/sql/$file")
    $global:LASTEXITCODE = 0
    $output = docker @args
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 0) { throw "Database SQL failed: $file" }
    $raw = ($output | ForEach-Object { [string]$_ }) -join "`n"
    foreach ($candidate in @($output | ForEach-Object { [string]$_ })) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $line = $candidate.Trim()
        if ($line.StartsWith('GA12_JSON:')) { $line = $line.Substring('GA12_JSON:'.Length) }
        if (-not $line.StartsWith('{')) { continue }
        try { return $line | ConvertFrom-Json } catch {}
    }
    $marker = 'GA12_JSON:'
    $markerIndex = $raw.LastIndexOf($marker)
    if ($markerIndex -ge 0) {
        $json = $raw.Substring($markerIndex + $marker.Length).Trim()
        try { return $json | ConvertFrom-Json } catch {}
    }
    $first = $raw.IndexOf('{')
    $last = $raw.LastIndexOf('}')
    if ($first -ge 0 -and $last -gt $first) {
        $json = $raw.Substring($first, $last - $first + 1)
        try { return $json | ConvertFrom-Json } catch {}
    }
    $sample = if ($raw.Length -gt 600) { $raw.Substring(0,600) } else { $raw }
    throw "No JSON result returned by $file. Raw output sample: $sample"
}
function Assert-DocumentContains([string]$Path, [string[]]$Terms) {
    Assert-True (Test-Path $Path) "Required document missing: $Path"
    $content = (Get-Content -Raw $Path).ToLowerInvariant()
    foreach ($term in $Terms) { Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path missing term: $term" }
}
function Require-Property($Object, [string]$Name) {
    Assert-True ($null -ne $Object) "Object missing while checking property $Name"
    $prop = $Object.PSObject.Properties[$Name]
    Assert-True ($null -ne $prop) "Required property missing: $Name"
    return $prop.Value
}
function Assert-2xx([int]$status, [string]$name) { Assert-True(($status -ge 200 -and $status -lt 300)) "$name must return 2xx; status=$status" }

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-Secret $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repo = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln = Join-Path $repo 'solidpos-platform.sln'
$sqlPath = Join-Path $scriptRoot 'ga-12-final-general-availability-launch-readiness-check.sql'
$ga11Script = Join-Path $scriptRoot 'validate-ga-11-customer-operator-admin-acceptance.ps1'
$ga11Manifest = Join-Path $repo '.runtime\ga-11-customer-operator-admin-acceptance\ga-11-customer-operator-admin-acceptance-manifest.json'
$secretScan = Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript = Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtime = Join-Path $repo '.runtime\ga-12-final-general-availability-launch-readiness'
$manifestPath = Join-Path $runtime 'ga-12-final-general-availability-launch-readiness-manifest.json'
$snapshotPath = Join-Path $runtime 'ga-12-final-general-availability-launch-readiness-snapshot.json'
$evidencePath = Join-Path $runtime 'ga-12-evidence.md'
$logPath = Join-Path $repo 'docs\ga\logs\ga-12-final-general-availability-launch-readiness-log.md'
New-Item -ItemType Directory -Force -Path @($runtime, (Split-Path $logPath)) | Out-Null

Write-Step "Validator version $script:Ga12ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plainPassword)) 'Password secure string resolved to empty/null. Re-run $securePassword = Read-Host ... -AsSecureString.'
Assert-True($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'

Write-Step 'Repository/document GA-12 guardrails...'
Assert-True(Test-Path $sln) 'solution missing'
Assert-True(Test-Path $sqlPath) 'GA-12 SQL check missing'
Assert-True(Test-Path $ga11Script) 'GA-11 prerequisite validator missing'
Assert-True(Test-Path $secretScan) 'secret scanner missing'
$docs = @(
    (Join-Path $repo 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
    (Join-Path $repo 'SOLIDPOS_GA_12_FINAL_GENERAL_AVAILABILITY_LAUNCH_READINESS.md'),
    (Join-Path $repo 'docs\ga\ga-12-final-general-availability-launch-readiness.md'),
    (Join-Path $repo 'docs\ga\ga-12-launch-decision-record.md'),
    (Join-Path $repo 'docs\ga\ga-12-evidence-matrix.md'),
    (Join-Path $repo 'docs\ga\ga-12-go-no-go.md')
)
foreach($doc in $docs){ Assert-True(Test-Path $doc) "Required GA-12 document missing: $doc" }
Assert-DocumentContains $docs[0] @('GA-11','PASS REAL PRODUCTION / GO GA-12','GA-12','NEXT','Concurrency 3+','waitingConnectionCount = 11','GENERAL AVAILABILITY: NOT ACTIVATED')
Assert-DocumentContains $docs[2] @('Final General Availability Launch Readiness','does not activate','schemaVersion=4','syncContract=schema_version_4','GO_CONTROLLED_GA_ROLLOUT')
Assert-DocumentContains $docs[3] @('GO_CONTROLLED_GA_ROLLOUT','Concurrency 3+','waitingConnectionCount = 11','does not flip')
Assert-DocumentContains $docs[5] @('PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT','public_ga_activation_requires_explicit_post_ga12_decision','NO_GO_FIX_BLOCKERS')
Write-Step 'Repository/document GA-12 guardrails PASS'

Write-Step 'Local build/test/secret guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln }
Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore }
Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Invoke-Checked 'secret scan' { & $secretScan }
if (-not $SkipDashboardBuild.IsPresent) {
    Assert-True(Test-Path $dashboardScript) 'PosDashboard validation script missing.'
    Invoke-Checked 'PosDashboard operations dashboard validation' { & $dashboardScript -BaseUrl $script:base -TenantId $TenantId -Email $Email -Password $Password }
    $dashboardBuildCondition = 'VALIDATED'
} else {
    Write-Step 'PosDashboard build skipped by switch.'
    $dashboardBuildCondition = 'SKIPPED_BY_SWITCH'
}
Write-Step 'Local build/test/secret guardrails PASS'

if (-not $SkipGa11Revalidation.IsPresent) {
    Write-Step 'Revalidating GA-11 prerequisite...'
    $ga11Args = @{ BaseUrl=$script:base; DashboardUrl=$DashboardUrl; TenantId=$TenantId; Email=$Email; Password=$Password; DatabaseUrl=$DatabaseUrl; SkipDashboardBuild=$SkipDashboardBuild; SkipGa10Revalidation=$true }
    & $ga11Script @ga11Args
    Write-Step 'GA-11 prerequisite revalidation PASS'
} else {
    Write-Step 'GA-11 prerequisite revalidation skipped by switch; GA-12 requires existing GA-11 PASS logs or runtime manifest.'
    if (Test-Path $ga11Manifest) {
        $ga11 = Get-Content -Raw $ga11Manifest | ConvertFrom-Json
        Assert-True(([string]$ga11.status).Contains('PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12')) 'GA-11 manifest status is not PASS / GO GA-12.'
        Assert-True(-not [bool]$ga11.generalAvailabilityActivated) 'GA-11 manifest indicates GA activated.'
    }
}

Write-Step 'Production final launch readiness API checks...'
$login = Invoke-Api Post '/api/v1/auth/login' @{ email = $Email; password = $plainPassword; tenantId = $TenantId }
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.accessToken)) 'Login access token missing'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$now = [DateTimeOffset]::UtcNow
$from = $now.AddDays(-7).ToString('o')
$to = $now.ToString('o')
$liveStatus = Get-HttpStatus Get '/health/live'
$readyStatus = Get-HttpStatus Get '/health/ready'
$unauthStatus = Get-HttpStatus Get '/api/v1/observability/metrics'
$tenantStatus = Get-HttpStatus Get '/api/v1/tenants/current' $headers
$salesStatus = Get-HttpStatus Get ("/api/v1/sales?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20") $headers
$dashboardOverviewStatus = Get-HttpStatus Get ("/api/v1/reports/dashboard/overview?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20&trendBucket=day") $headers
$syncStatusCode = Get-HttpStatus Get '/api/v1/sync/status' $headers
$syncContract = Invoke-Api Get '/api/v1/sync/contract' $null $headers
$metrics = Invoke-Api Get '/api/v1/observability/metrics' $null $headers
if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    $dashStatus = Get-HttpStatus Get $DashboardUrl @{} $null 30 -Absolute
    Assert-True(($dashStatus -ge 200 -and $dashStatus -lt 400)) "DashboardUrl must return 2xx/3xx; status=$dashStatus"
} else { $dashStatus = 0 }
Assert-True($liveStatus -eq 200) "health/live must return 200; status=$liveStatus"
Assert-True($readyStatus -eq 200) "health/ready must return 200; status=$readyStatus"
Assert-True($unauthStatus -eq 401) "observability/metrics without auth must return 401; status=$unauthStatus"
foreach($pair in @(@($tenantStatus,'tenant-current'),@($salesStatus,'sales-list'),@($dashboardOverviewStatus,'dashboard-overview'),@($syncStatusCode,'sync-status'))) { Assert-2xx ([int]$pair[0]) ([string]$pair[1]) }
Assert-True([int]$syncContract.currentSchemaVersion -eq 4) 'Sync contract currentSchemaVersion must remain 4.'
$dbMetrics = Require-Property $metrics 'database'
Assert-True([bool](Require-Property $dbMetrics 'ready')) 'Observability database.ready must be true.'
Write-Step 'Production final launch readiness API checks PASS'

Write-Step 'Database final launch readiness snapshot...'
$sql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId }
Assert-True([int]$sql.schemaVersion -eq 4) 'Database snapshot schemaVersion must be 4.'
Assert-True([string]$sql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must be schema_version_4.'
Assert-True([bool]$sql.requiredTablesPresent) "Missing required launch readiness source tables: $($sql.missingRequiredTables -join ', ')"
Assert-True(-not [bool]$sql.generalAvailabilityActivated) 'GA-12 SQL indicates General Availability already activated.'
Assert-True([long]$sql.tenantState.active_tenant_count -eq 1) 'Tenant must be active for GA-12.'
Assert-True([long]$sql.launchBase.active_store_count -gt 0) 'At least one active store is required.'
Assert-True([long]$sql.launchBase.available_terminal_count -gt 0) 'At least one terminal is required.'
Assert-True([long]$sql.launchBase.active_sellable_product_count -gt 0) 'At least one active sellable product is required.'
Assert-True([long]$sql.launchBase.active_payment_method_count -gt 0) 'At least one active payment method is required.'
Assert-True([long]$sql.launchBase.active_user_count -gt 0) 'At least one active user is required.'
Assert-True([long]$sql.launchBase.active_stable_release_count -ge 1) 'At least one active stable release is required.'
Assert-True([long]$sql.syncIntegrity.legacy_schema_event_count -eq 0) 'Legacy schema event count must be 0.'
Assert-True([long]$sql.syncIntegrity.pending_conflict_count -eq 0) 'Pending conflict count must be 0.'
Assert-True([long]$sql.syncIntegrity.stale_processing_count -eq 0) 'Stale processing count must be 0.'
Assert-True([long]$sql.financialIntegrity.duplicate_local_sale_count -eq 0) 'Duplicate local sale count must be 0.'
Assert-True([long]$sql.financialIntegrity.negative_payment_count -eq 0) 'Negative payment count must be 0.'
Assert-True([long]$sql.financialIntegrity.sale_payment_mismatch_count -eq 0) 'Sale payment mismatch count must be 0.'
Assert-True([long]$sql.financialIntegrity.return_refund_mismatch_count -eq 0) 'Return/refund mismatch count must be 0.'
Assert-True([long]$sql.rls.rls_missing_table_count -eq 0) 'RLS missing table count must be 0.'
Assert-True([long]$sql.databasePressure.long_running_query_count -eq 0) 'Long running query count must be 0.'
Write-Step 'Database final launch readiness snapshot PASS'

Write-Step 'GA-12 final blocker and decision matrix...'
$blockers = @()
$conditions = @(
    'ga09_capacity_boundary_concurrency_3_plus_upstream_error_carried_forward',
    'ga10_ga11_db_waiting_connections_observation_carried_forward',
    'public_ga_activation_requires_explicit_post_ga12_decision'
)
if ($SkipDashboardBuild.IsPresent) { $conditions += 'dashboard_build_skipped' }
if ($SkipGa11Revalidation.IsPresent) { $conditions += 'ga11_revalidation_skipped_requires_external_ga11_pass_log' }
if ([long]$sql.databasePressure.waiting_connection_count -gt 0) { $conditions += "db_waiting_connections_$($sql.databasePressure.waiting_connection_count)" }
if ([long]$sql.launchBase.open_shift_count -gt 0) { $conditions += "open_shift_count_$($sql.launchBase.open_shift_count)_requires_launch_attention" }
if ([int]$syncContract.currentSchemaVersion -ne 4) { $blockers += 'sync_contract_schema_drift' }
if ([bool]$sql.requiredTablesPresent -ne $true) { $blockers += 'launch_readiness_source_tables_missing' }
if ([long]$sql.launchBase.active_stable_release_count -lt 1) { $blockers += 'active_stable_release_missing' }
if ([long]$sql.syncIntegrity.legacy_schema_event_count -ne 0) { $blockers += 'legacy_schema_events' }
if ([long]$sql.syncIntegrity.pending_conflict_count -ne 0) { $blockers += 'pending_sync_conflicts' }
if ([long]$sql.syncIntegrity.stale_processing_count -ne 0) { $blockers += 'stale_sync_processing' }
if ([long]$sql.financialIntegrity.duplicate_local_sale_count -ne 0) { $blockers += 'duplicate_local_sales' }
if ([long]$sql.rls.rls_missing_table_count -ne 0) { $blockers += 'rls_drift' }
if ([bool]$sql.generalAvailabilityActivated -eq $true) { $blockers += 'general_availability_already_activated' }
Assert-True($blockers.Count -eq 0) "GA-12 BLOCKED: $($blockers -join ', ')"
$finalDecision = 'GO_CONTROLLED_GA_ROLLOUT'
Write-Step 'GA-12 final blocker and decision matrix PASS'

$logoutStatus = Get-HttpStatus Post '/api/v1/auth/logout' $headers @{ refreshToken = [string]$login.refreshToken; tenantId = $TenantId }
Assert-True($logoutStatus -eq 204) "GA-12 session logout must return 204; status=$logoutStatus"

$generated = (Get-Date).ToUniversalTime().ToString('o')
$manifest = [ordered]@{
    phase = 'GA-12'
    status = 'PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT'
    tenantId = $TenantId
    baseUrl = $script:base
    dashboardUrl = $DashboardUrl
    generatedAt = $generated
    validatorVersion = $script:Ga12ValidatorVersion
    entryGate = 'PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12'
    finalDecision = $finalDecision
    launchAuthorizationOnly = $true
    publicGeneralAvailabilityActivated = $false
    generalAvailabilityActivated = $false
    knownCapacityCondition = 'GA-09 PASS at Concurrency 1/2; Concurrency 3+ current Railway/upstream path can return 400 upstream error.'
    knownDbCondition = 'GA-10/GA-11 observed waiting connections; monitor/tune pool/connections before public GA launch.'
    healthLiveStatus = $liveStatus
    healthReadyStatus = $readyStatus
    unauthenticatedObservabilityStatus = $unauthStatus
    tenantCurrentStatus = $tenantStatus
    salesListStatus = $salesStatus
    dashboardOverviewStatus = $dashboardOverviewStatus
    syncStatusCode = $syncStatusCode
    dashboardUrlStatus = $dashStatus
    dashboardBuild = $dashboardBuildCondition
    metricsDatabaseReady = [bool]$dbMetrics.ready
    metricsTotalRequests = $metrics.requests.total
    metricsFailedRequests = $metrics.requests.failed
    metricsP95LatencyMs = $metrics.requests.p95LatencyMs
    syncContractCurrentSchemaVersion = [int]$syncContract.currentSchemaVersion
    databaseSnapshot = $sql
    activeStoreCount = [long]$sql.launchBase.active_store_count
    availableTerminalCount = [long]$sql.launchBase.available_terminal_count
    activeSellableProductCount = [long]$sql.launchBase.active_sellable_product_count
    activePaymentMethodCount = [long]$sql.launchBase.active_payment_method_count
    activeUserCount = [long]$sql.launchBase.active_user_count
    activeStableReleaseCount = [long]$sql.launchBase.active_stable_release_count
    openShiftCount = [long]$sql.launchBase.open_shift_count
    duplicateLocalSaleCount = [long]$sql.financialIntegrity.duplicate_local_sale_count
    legacySchemaEventCount = [long]$sql.syncIntegrity.legacy_schema_event_count
    pendingConflictCount = [long]$sql.syncIntegrity.pending_conflict_count
    staleProcessingCount = [long]$sql.syncIntegrity.stale_processing_count
    rlsMissingTableCount = [long]$sql.rls.rls_missing_table_count
    waitingConnectionCount = [long]$sql.databasePressure.waiting_connection_count
    longRunningQueryCount = [long]$sql.databasePressure.long_running_query_count
    blockers = @()
    conditions = $conditions
    schemaVersion = 4
    syncContract = 'schema_version_4'
    nextStep = 'Explicit post-GA-12 launch decision: controlled rollout, capacity scale-up, or no-go remediation.'
}
$manifest | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{ manifest=$manifest; databaseSnapshot=$sql; metrics=$metrics } | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-12 Final General Availability Launch Readiness Evidence

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Ga12ValidatorVersion
- finalDecision: $finalDecision
- launchAuthorizationOnly: True
- publicGeneralAvailabilityActivated: False
- healthLiveStatus: $liveStatus
- healthReadyStatus: $readyStatus
- dashboardUrlStatus: $dashStatus
- syncContractCurrentSchemaVersion: $($manifest.syncContractCurrentSchemaVersion)
- activeStableReleaseCount: $($manifest.activeStableReleaseCount)
- duplicateLocalSaleCount: $($manifest.duplicateLocalSaleCount)
- legacySchemaEventCount: $($manifest.legacySchemaEventCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
- staleProcessingCount: $($manifest.staleProcessingCount)
- rlsMissingTableCount: $($manifest.rlsMissingTableCount)
- waitingConnectionCount: $($manifest.waitingConnectionCount)
- longRunningQueryCount: $($manifest.longRunningQueryCount)
- blockers: {}
- conditions: $($conditions -join ', ')
- knownCapacityCondition: $($manifest.knownCapacityCondition)
- knownDbCondition: $($manifest.knownDbCondition)
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $evidencePath
@"
# GA-12 Final General Availability Launch Readiness Log

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Ga12ValidatorVersion
- finalDecision: $finalDecision
- blockers: {}
- conditions: $($conditions -join ', ')
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath

$plainPassword = $null
[GC]::Collect()
Write-Step 'GA-12 evidence manifest and final readiness snapshot PASS'
Write-Step 'PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT'
[pscustomobject]$manifest
