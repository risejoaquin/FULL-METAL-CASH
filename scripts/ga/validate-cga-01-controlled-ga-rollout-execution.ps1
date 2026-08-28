param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('LIMITED')][string]$RolloutMode = 'LIMITED',
    [int]$MaxStores = 2,
    [int]$MaxConcurrentTerminals = 2,
    [int]$ObservationWindowHours = 24,
    [switch]$SkipDashboardBuild,
    [switch]$SkipPostGa12Revalidation
)

$ErrorActionPreference = 'Stop'
$script:Cga01ValidatorVersion = 'CGA-01.0-controlled-ga-rollout-execution'
function Write-Step([string]$message) { Write-Host "[CGA-01] $message" }
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
        if ($line.StartsWith('CGA01_JSON:')) { $line = $line.Substring('CGA01_JSON:'.Length) }
        if (-not $line.StartsWith('{')) { continue }
        try { return $line | ConvertFrom-Json } catch {}
    }
    $marker = 'CGA01_JSON:'
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
$sqlPath = Join-Path $scriptRoot 'cga-01-controlled-ga-rollout-execution-check.sql'
$postGa12Script = Join-Path $scriptRoot 'validate-post-ga-12-launch-decision.ps1'
$postGa12Manifest = Join-Path $repo '.runtime\post-ga-12-launch-decision\post-ga-12-launch-decision-manifest.json'
$secretScan = Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript = Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtime = Join-Path $repo '.runtime\cga-01-controlled-ga-rollout-execution'
$manifestPath = Join-Path $runtime 'cga-01-controlled-ga-rollout-execution-manifest.json'
$snapshotPath = Join-Path $runtime 'cga-01-controlled-ga-rollout-execution-snapshot.json'
$evidencePath = Join-Path $runtime 'cga-01-controlled-ga-rollout-execution-evidence.md'
$logPath = Join-Path $repo 'docs\ga\logs\cga-01-controlled-ga-rollout-execution-log.md'
New-Item -ItemType Directory -Force -Path @($runtime, (Split-Path $logPath)) | Out-Null

Write-Step "Validator version $script:Cga01ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plainPassword)) 'Password secure string resolved to empty/null. Re-run $securePassword = Read-Host ... -AsSecureString.'
Assert-True($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'
Assert-True($RolloutMode -eq 'LIMITED') 'CGA-01 only supports LIMITED rollout mode.'
Assert-True($MaxStores -ge 1 -and $MaxStores -le 2) 'CGA-01 MaxStores must be between 1 and 2.'
Assert-True($MaxConcurrentTerminals -ge 1 -and $MaxConcurrentTerminals -le 2) 'CGA-01 MaxConcurrentTerminals must be between 1 and 2.'
Assert-True($ObservationWindowHours -ge 1 -and $ObservationWindowHours -le 72) 'CGA-01 ObservationWindowHours must be between 1 and 72.'

Write-Step 'Repository/document CGA-01 guardrails...'
Assert-True(Test-Path $sln) 'solution missing'
Assert-True(Test-Path $sqlPath) 'CGA-01 SQL check missing'
Assert-True(Test-Path $postGa12Script) 'Post-GA-12 prerequisite validator missing'
Assert-True(Test-Path $secretScan) 'secret scanner missing'
$docs = @(
    (Join-Path $repo 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
    (Join-Path $repo 'SOLIDPOS_CGA_01_CONTROLLED_GA_ROLLOUT_EXECUTION.md'),
    (Join-Path $repo 'docs\ga\cga-01-controlled-ga-rollout-execution.md'),
    (Join-Path $repo 'docs\ga\cga-01-rollout-scope.md'),
    (Join-Path $repo 'docs\ga\cga-01-monitoring-runbook.md'),
    (Join-Path $repo 'docs\ga\cga-01-evidence-matrix.md'),
    (Join-Path $repo 'docs\ga\cga-01-go-no-go.md')
)
foreach($doc in $docs){ Assert-True(Test-Path $doc) "Required CGA-01 document missing: $doc" }
Assert-DocumentContains $docs[0] @('POST-GA-12','CONTROLLED_ROLLOUT','CGA-01','PUBLIC GA: NOT ACTIVATED')
Assert-DocumentContains $docs[2] @('CGA-01','Controlled GA Rollout Execution','LIMITED','does not activate','publicGeneralAvailabilityActivated=False','limit=20','trendBucket=day')
Assert-DocumentContains $docs[3] @('MaxStores','MaxConcurrentTerminals','ObservationWindowHours','LIMITED')
Assert-DocumentContains $docs[4] @('health/ready','observability','sync/status','waiting connections','rollback')
Assert-DocumentContains $docs[5] @('dashboard overview','sales range','schemaVersion 4','syncContract schema_version_4')
Assert-DocumentContains $docs[6] @('PASS','BLOCKED','NO-GO','PUBLIC GA: NOT ACTIVATED')
Write-Step 'Repository/document CGA-01 guardrails PASS'

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

if (-not $SkipPostGa12Revalidation.IsPresent) {
    Write-Step 'Revalidating Post-GA-12 prerequisite...'
    $postArgs = @{ BaseUrl=$script:base; DashboardUrl=$DashboardUrl; TenantId=$TenantId; Email=$Email; Password=$Password; DatabaseUrl=$DatabaseUrl; Decision='CONTROLLED_ROLLOUT'; SkipDashboardBuild=$SkipDashboardBuild; SkipGa12Revalidation=$true }
    & $postGa12Script @postArgs
    Write-Step 'Post-GA-12 prerequisite revalidation PASS'
} else {
    Write-Step 'Post-GA-12 prerequisite revalidation skipped by switch; CGA-01 requires existing Post-GA-12 PASS logs or runtime manifest.'
    if (Test-Path $postGa12Manifest) {
        $post = Get-Content -Raw $postGa12Manifest | ConvertFrom-Json
        Assert-True(([string]$post.status).Contains('PASS POST-GA-12 CONTROLLED ROLLOUT DECISION')) 'Post-GA-12 manifest status is not PASS controlled rollout.'
        Assert-True([bool]$post.controlledRolloutAllowed) 'Post-GA-12 manifest does not allow controlled rollout.'
        Assert-True(-not [bool]$post.generalAvailabilityActivated) 'Post-GA-12 manifest indicates GA activated.'
    }
}

Write-Step 'Production controlled rollout API checks...'
$login = Invoke-Api Post '/api/v1/auth/login' @{ email = $Email; password = $plainPassword; tenantId = $TenantId }
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.accessToken)) 'Login access token missing'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$now = [DateTimeOffset]::UtcNow
$from = $now.AddDays(-7).ToString('o')
$to = $now.ToString('o')
$fromEsc = [uri]::EscapeDataString($from)
$toEsc = [uri]::EscapeDataString($to)
$liveStatus = Get-HttpStatus Get '/health/live'
$readyStatus = Get-HttpStatus Get '/health/ready'
$unauthObs = Get-HttpStatus Get '/api/v1/observability/metrics'
$tenant = Invoke-Api Get '/api/v1/tenants/current' $null $headers
$tenantStatus = 200
$stores = Invoke-Api Get '/api/v1/stores' $null $headers
$usersStatus = Get-HttpStatus Get '/api/v1/users' $headers
$rolesStatus = Get-HttpStatus Get '/api/v1/roles' $headers
$permissionsStatus = Get-HttpStatus Get '/api/v1/permissions' $headers
$catalogStatus = Get-HttpStatus Get '/api/v1/tenant/catalog' $headers
$salesStatus = Get-HttpStatus Get ("/api/v1/sales?from=$fromEsc&to=$toEsc&limit=20") $headers
$salesRange = Invoke-Api Get ("/api/v1/reports/sales/range?from=$fromEsc&to=$toEsc") $null $headers
$dashboardOverview = Invoke-Api Get ("/api/v1/reports/dashboard/overview?from=$fromEsc&to=$toEsc&limit=20&trendBucket=day") $null $headers
$dashboardOverviewStatus = 200
$mainStoreOverviewStatus = 0
$secondStoreOverviewStatus = 0
$activeStores = @($stores | Where-Object { $_.status -eq 'active' })
if ($activeStores.Count -gt 0) {
    $mainStoreId = [string]$activeStores[0].id
    $mainStoreOverviewStatus = Get-HttpStatus Get ("/api/v1/reports/dashboard/overview?storeId=$mainStoreId&from=$fromEsc&to=$toEsc&limit=20&trendBucket=day") $headers
}
if ($activeStores.Count -gt 1) {
    $secondStoreId = [string]$activeStores[1].id
    $secondStoreOverviewStatus = Get-HttpStatus Get ("/api/v1/reports/dashboard/overview?storeId=$secondStoreId&from=$fromEsc&to=$toEsc&limit=20&trendBucket=day") $headers
}
$syncStatus = Invoke-Api Get '/api/v1/sync/status' $null $headers
$syncStatusCode = 200
$syncContract = Invoke-Api Get '/api/v1/sync/contract' $null $headers
$metrics = Invoke-Api Get '/api/v1/observability/metrics' $null $headers
if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    $dashStatus = Get-HttpStatus Get $DashboardUrl @{} $null 30 -Absolute
    Assert-True(($dashStatus -ge 200 -and $dashStatus -lt 400)) "DashboardUrl must return 2xx/3xx; status=$dashStatus"
} else { $dashStatus = 0 }
Assert-True($liveStatus -eq 200) "health/live must return 200; status=$liveStatus"
Assert-True($readyStatus -eq 200) "health/ready must return 200; status=$readyStatus"
Assert-True($unauthObs -eq 401) "observability/metrics without auth must return 401; status=$unauthObs"
foreach($pair in @(@($tenantStatus,'tenant-current'),@($usersStatus,'users'),@($rolesStatus,'roles'),@($permissionsStatus,'permissions'),@($catalogStatus,'catalog'),@($salesStatus,'sales-list'),@($dashboardOverviewStatus,'dashboard-overview'),@($syncStatusCode,'sync-status'))) { Assert-2xx ([int]$pair[0]) ([string]$pair[1]) }
if ($mainStoreOverviewStatus -ne 0) { Assert-2xx $mainStoreOverviewStatus 'dashboard-overview-main-store' }
if ($secondStoreOverviewStatus -ne 0) { Assert-2xx $secondStoreOverviewStatus 'dashboard-overview-second-store' }
Assert-True([int]$syncContract.currentSchemaVersion -eq 4) 'Sync contract currentSchemaVersion must remain 4.'
Assert-True($tenant.status -eq 'active') 'Tenant must be active.'
Assert-True($activeStores.Count -ge 1) 'At least one active store is required for CGA-01.'
Assert-True($activeStores.Count -le $MaxStores) "CGA-01 active store count exceeds MaxStores. active=$($activeStores.Count) max=$MaxStores"
$dbMetrics = Require-Property $metrics 'database'
Assert-True([bool](Require-Property $dbMetrics 'ready')) 'Observability database.ready must be true.'
Assert-True(($dashboardOverview.PSObject.Properties['sales'] -ne $null)) 'Dashboard overview must include sales section.'
Assert-True(($dashboardOverview.PSObject.Properties['paymentMethods'] -ne $null)) 'Dashboard overview must include paymentMethods.'
Assert-True(($dashboardOverview.PSObject.Properties['topProducts'] -ne $null)) 'Dashboard overview must include topProducts.'
Assert-True(($dashboardOverview.PSObject.Properties['inventory'] -ne $null)) 'Dashboard overview must include inventory.'
Write-Step 'Production controlled rollout API checks PASS'

Write-Step 'Database controlled rollout snapshot...'
$sql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId; max_stores = $MaxStores; max_concurrent_terminals = $MaxConcurrentTerminals; observation_window_hours = $ObservationWindowHours }
Assert-True([int]$sql.schemaVersion -eq 4) 'Database snapshot schemaVersion must be 4.'
Assert-True([string]$sql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must be schema_version_4.'
Assert-True([bool]$sql.requiredTablesPresent) "Missing required CGA-01 source tables: $($sql.missingRequiredTables -join ', ')"
Assert-True(-not [bool]$sql.generalAvailabilityActivated) 'CGA-01 SQL indicates General Availability already activated.'
Assert-True([long]$sql.tenantState.active_tenant_count -eq 1) 'Tenant must be active for CGA-01.'
Assert-True([long]$sql.rolloutScope.active_store_count -ge 1) 'At least one active store is required.'
Assert-True([long]$sql.rolloutScope.active_store_count -le $MaxStores) 'Active store count exceeds controlled rollout MaxStores.'
Assert-True([long]$sql.rolloutScope.open_shift_count -le $MaxConcurrentTerminals) 'Open shift count exceeds controlled rollout MaxConcurrentTerminals.'
Assert-True([long]$sql.rolloutScope.active_stable_release_count -ge 1) 'At least one active stable release is required.'
Assert-True([long]$sql.syncIntegrity.legacy_schema_event_count -eq 0) 'Legacy schema event count must be 0.'
Assert-True([long]$sql.syncIntegrity.pending_conflict_count -eq 0) 'Pending conflict count must be 0.'
Assert-True([long]$sql.syncIntegrity.retry_pending_count -eq 0) 'Retry pending sync count must be 0.'
Assert-True([long]$sql.syncIntegrity.stale_processing_count -eq 0) 'Stale processing count must be 0.'
Assert-True([long]$sql.financialIntegrity.duplicate_local_sale_count -eq 0) 'Duplicate local sale count must be 0.'
Assert-True([long]$sql.financialIntegrity.negative_payment_count -eq 0) 'Negative payment count must be 0.'
Assert-True([long]$sql.rls.rls_missing_table_count -eq 0) 'RLS missing table count must be 0.'
Assert-True([long]$sql.databasePressure.long_running_query_count -eq 0) 'Long running query count must be 0.'
Write-Step 'Database controlled rollout snapshot PASS'

Write-Step 'CGA-01 blocker matrix...'
$blockers = @()
$conditions = @(
    'ga09_capacity_boundary_concurrency_3_plus_upstream_error_carried_forward',
    'ga10_ga11_ga12_db_waiting_connections_observation_carried_forward',
    'public_ga_activation_requires_explicit_separate_change',
    'dashboard_overview_requires_from_to_limit_trendBucket_contract'
)
if ($SkipDashboardBuild.IsPresent) { $conditions += 'dashboard_build_skipped' }
if ($SkipPostGa12Revalidation.IsPresent) { $conditions += 'post_ga12_revalidation_skipped_requires_external_post_ga12_pass_log' }
if ([long]$sql.databasePressure.waiting_connection_count -gt 0) { $conditions += "db_waiting_connections_$($sql.databasePressure.waiting_connection_count)" }
if ([long]$sql.syncIntegrity.dead_letter_count -gt 0) { $conditions += "historical_dead_letter_events_$($sql.syncIntegrity.dead_letter_count)" }
if (($null -ne $metrics.inventory) -and ([long]$metrics.inventory.lowStockItemCount -gt 0)) { $conditions += "low_stock_items_$($metrics.inventory.lowStockItemCount)" }
if ([int]$syncContract.currentSchemaVersion -ne 4) { $blockers += 'sync_contract_schema_drift' }
if ([bool]$sql.requiredTablesPresent -ne $true) { $blockers += 'cga01_source_tables_missing' }
if ([long]$sql.rolloutScope.active_store_count -gt $MaxStores) { $blockers += 'controlled_rollout_store_scope_exceeded' }
if ([long]$sql.rolloutScope.open_shift_count -gt $MaxConcurrentTerminals) { $blockers += 'controlled_rollout_terminal_scope_exceeded' }
if ([long]$sql.syncIntegrity.legacy_schema_event_count -ne 0) { $blockers += 'legacy_schema_events' }
if ([long]$sql.syncIntegrity.pending_conflict_count -ne 0) { $blockers += 'pending_sync_conflicts' }
if ([long]$sql.syncIntegrity.retry_pending_count -ne 0) { $blockers += 'retry_pending_sync_events' }
if ([long]$sql.syncIntegrity.stale_processing_count -ne 0) { $blockers += 'stale_sync_processing' }
if ([long]$sql.financialIntegrity.duplicate_local_sale_count -ne 0) { $blockers += 'duplicate_local_sales' }
if ([long]$sql.financialIntegrity.negative_payment_count -ne 0) { $blockers += 'negative_payments' }
if ([long]$sql.rls.rls_missing_table_count -ne 0) { $blockers += 'rls_drift' }
if ([bool]$sql.generalAvailabilityActivated -eq $true) { $blockers += 'public_general_availability_activated_without_explicit_decision' }
Assert-True($blockers.Count -eq 0) "CGA-01 BLOCKED: $($blockers -join ', ')"
$status = 'PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02'
Write-Step 'CGA-01 blocker matrix PASS'

$logoutStatus = Get-HttpStatus Post '/api/v1/auth/logout' $headers @{ refreshToken = [string]$login.refreshToken; tenantId = $TenantId }
Assert-True($logoutStatus -eq 204) "CGA-01 session logout must return 204; status=$logoutStatus"

$generated = (Get-Date).ToUniversalTime().ToString('o')
$manifest = [ordered]@{
    phase = 'CGA-01'
    status = $status
    tenantId = $TenantId
    baseUrl = $script:base
    dashboardUrl = $DashboardUrl
    generatedAt = $generated
    validatorVersion = $script:Cga01ValidatorVersion
    entryGate = 'PASS POST-GA-12 CONTROLLED ROLLOUT DECISION / READY FOR LIMITED GA EXECUTION'
    rolloutMode = $RolloutMode
    maxStores = $MaxStores
    maxConcurrentTerminals = $MaxConcurrentTerminals
    observationWindowHours = $ObservationWindowHours
    controlledRolloutExecution = 'PASS'
    controlledRolloutAllowed = $true
    launchAuthorizationOnly = $true
    publicGeneralAvailabilityActivated = $false
    generalAvailabilityActivated = $false
    knownCapacityCondition = 'GA-09 PASS at Concurrency 1/2; Concurrency 3+ current Railway/upstream path can return 400 upstream error.'
    knownDbCondition = 'GA-10/GA-11/GA-12/Post-GA-12 observed waiting connections; monitor/tune pool/connections before public GA launch.'
    dashboardOverviewContract = 'from,to,limit,trendBucket required; storeId optional.'
    healthLiveStatus = $liveStatus
    healthReadyStatus = $readyStatus
    unauthenticatedObservabilityStatus = $unauthObs
    tenantCurrentStatus = $tenantStatus
    usersStatus = $usersStatus
    rolesStatus = $rolesStatus
    permissionsStatus = $permissionsStatus
    catalogStatus = $catalogStatus
    salesListStatus = $salesStatus
    salesRangeCompletedSalesCount = $salesRange.completedSalesCount
    dashboardOverviewStatus = $dashboardOverviewStatus
    dashboardOverviewCompletedSalesCount = $dashboardOverview.sales.completedSalesCount
    mainStoreOverviewStatus = $mainStoreOverviewStatus
    secondStoreOverviewStatus = $secondStoreOverviewStatus
    syncStatusCode = $syncStatusCode
    dashboardUrlStatus = $dashStatus
    dashboardBuild = $dashboardBuildCondition
    metricsDatabaseReady = [bool]$dbMetrics.ready
    metricsP95LatencyMs = $metrics.requests.p95LatencyMs
    syncContractCurrentSchemaVersion = [int]$syncContract.currentSchemaVersion
    databaseSnapshot = $sql
    activeStoreCount = [long]$sql.rolloutScope.active_store_count
    availableTerminalCount = [long]$sql.rolloutScope.available_terminal_count
    openShiftCount = [long]$sql.rolloutScope.open_shift_count
    activeStableReleaseCount = [long]$sql.rolloutScope.active_stable_release_count
    completedSalesLast7Days = [long]$sql.rolloutActivity.completed_sales_last_7_days
    duplicateLocalSaleCount = [long]$sql.financialIntegrity.duplicate_local_sale_count
    legacySchemaEventCount = [long]$sql.syncIntegrity.legacy_schema_event_count
    pendingConflictCount = [long]$sql.syncIntegrity.pending_conflict_count
    retryPendingCount = [long]$sql.syncIntegrity.retry_pending_count
    deadLetterCount = [long]$sql.syncIntegrity.dead_letter_count
    staleProcessingCount = [long]$sql.syncIntegrity.stale_processing_count
    rlsMissingTableCount = [long]$sql.rls.rls_missing_table_count
    waitingConnectionCount = [long]$sql.databasePressure.waiting_connection_count
    longRunningQueryCount = [long]$sql.databasePressure.long_running_query_count
    blockers = @()
    conditions = $conditions
    schemaVersion = 4
    syncContract = 'schema_version_4'
    publicGaActivation = 'NOT_ACTIVATED'
    nextPhase = 'CGA-02 - 24h/72h Production Monitoring and Incident Window'
}
$manifest | ConvertTo-Json -Depth 60 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{ manifest=$manifest; databaseSnapshot=$sql; metrics=$metrics; dashboardOverview=$dashboardOverview; salesRange=$salesRange; syncStatus=$syncStatus } | ConvertTo-Json -Depth 60 | Set-Content -Encoding UTF8 $snapshotPath
@"
# CGA-01 Controlled GA Rollout Execution Evidence

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Cga01ValidatorVersion
- rolloutMode: $RolloutMode
- maxStores: $MaxStores
- maxConcurrentTerminals: $MaxConcurrentTerminals
- observationWindowHours: $ObservationWindowHours
- publicGeneralAvailabilityActivated: False
- generalAvailabilityActivated: False
- healthLiveStatus: $liveStatus
- healthReadyStatus: $readyStatus
- dashboardUrlStatus: $dashStatus
- dashboardOverviewStatus: $dashboardOverviewStatus
- dashboardOverviewContract: from,to,limit,trendBucket required; storeId optional
- syncContractCurrentSchemaVersion: $($manifest.syncContractCurrentSchemaVersion)
- activeStoreCount: $($manifest.activeStoreCount)
- openShiftCount: $($manifest.openShiftCount)
- completedSalesLast7Days: $($manifest.completedSalesLast7Days)
- duplicateLocalSaleCount: $($manifest.duplicateLocalSaleCount)
- legacySchemaEventCount: $($manifest.legacySchemaEventCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
- retryPendingCount: $($manifest.retryPendingCount)
- deadLetterCount: $($manifest.deadLetterCount)
- staleProcessingCount: $($manifest.staleProcessingCount)
- rlsMissingTableCount: $($manifest.rlsMissingTableCount)
- waitingConnectionCount: $($manifest.waitingConnectionCount)
- longRunningQueryCount: $($manifest.longRunningQueryCount)
- blockers: {}
- conditions: $($conditions -join ', ')
- schemaVersion: 4
- syncContract: schema_version_4
- nextPhase: $($manifest.nextPhase)
"@ | Set-Content -Encoding UTF8 $evidencePath
@"
# CGA-01 Controlled GA Rollout Execution Log

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Cga01ValidatorVersion
- rolloutMode: $RolloutMode
- blockers: {}
- conditions: $($conditions -join ', ')
- publicGeneralAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath

$plainPassword = $null
[GC]::Collect()
Write-Step 'CGA-01 evidence manifest and controlled rollout snapshot PASS'
Write-Step $status
[pscustomobject]$manifest
