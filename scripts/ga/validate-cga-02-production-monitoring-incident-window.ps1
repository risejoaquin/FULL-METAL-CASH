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
    [ValidateSet(24,72)][int]$MonitoringWindowHours = 24,
    [int]$SampleCount = 3,
    [int]$SampleIntervalSeconds = 5,
    [int]$AllowedExistingSyncConflictCount = 0,
    [switch]$SkipDashboardBuild,
    [switch]$SkipCga01Revalidation
)

$ErrorActionPreference = 'Stop'
$script:Cga02ValidatorVersion = 'CGA-02.1-production-monitoring-incident-window-known-conflict-baseline'
function Write-Step([string]$message) { Write-Host "[CGA-02] $message" }
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
        if ($line.StartsWith('CGA02_JSON:')) { $line = $line.Substring('CGA02_JSON:'.Length) }
        if (-not $line.StartsWith('{')) { continue }
        try { return $line | ConvertFrom-Json } catch {}
    }
    $marker = 'CGA02_JSON:'
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
$sqlPath = Join-Path $scriptRoot 'cga-02-production-monitoring-incident-window-check.sql'
$cga01Script = Join-Path $scriptRoot 'validate-cga-01-controlled-ga-rollout-execution.ps1'
$cga01Manifest = Join-Path $repo '.runtime\cga-01-controlled-ga-rollout-execution\cga-01-controlled-ga-rollout-execution-manifest.json'
$secretScan = Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript = Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtime = Join-Path $repo '.runtime\cga-02-production-monitoring-incident-window'
$manifestPath = Join-Path $runtime 'cga-02-production-monitoring-incident-window-manifest.json'
$snapshotPath = Join-Path $runtime 'cga-02-production-monitoring-incident-window-snapshot.json'
$evidencePath = Join-Path $runtime 'cga-02-production-monitoring-incident-window-evidence.md'
$logPath = Join-Path $repo 'docs\ga\logs\cga-02-production-monitoring-incident-window-log.md'
New-Item -ItemType Directory -Force -Path @($runtime, (Split-Path $logPath)) | Out-Null

Write-Step "Validator version $script:Cga02ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plainPassword)) 'Password secure string resolved to empty/null. Re-run $securePassword = Read-Host ... -AsSecureString.'
Assert-True($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'
Assert-True($RolloutMode -eq 'LIMITED') 'CGA-02 only supports LIMITED rollout mode.'
Assert-True($MaxStores -ge 1 -and $MaxStores -le 2) 'CGA-02 MaxStores must be between 1 and 2.'
Assert-True($MaxConcurrentTerminals -ge 1 -and $MaxConcurrentTerminals -le 2) 'CGA-02 MaxConcurrentTerminals must be between 1 and 2.'
Assert-True($MonitoringWindowHours -in @(24,72)) 'CGA-02 MonitoringWindowHours must be 24 or 72.'
Assert-True($SampleCount -ge 1 -and $SampleCount -le 12) 'CGA-02 SampleCount must be between 1 and 12.'
Assert-True($SampleIntervalSeconds -ge 0 -and $SampleIntervalSeconds -le 300) 'CGA-02 SampleIntervalSeconds must be between 0 and 300.'
Assert-True($AllowedExistingSyncConflictCount -ge 0 -and $AllowedExistingSyncConflictCount -le 25) 'CGA-02 AllowedExistingSyncConflictCount must be between 0 and 25.'

Write-Step 'Repository/document CGA-02 guardrails...'
Assert-True(Test-Path $sln) 'solution missing'
Assert-True(Test-Path $sqlPath) 'CGA-02 SQL check missing'
Assert-True(Test-Path $cga01Script) 'CGA-01 prerequisite validator missing'
Assert-True(Test-Path $secretScan) 'secret scanner missing'
$docs = @(
    (Join-Path $repo 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
    (Join-Path $repo 'SOLIDPOS_CGA_02_PRODUCTION_MONITORING_INCIDENT_WINDOW.md'),
    (Join-Path $repo 'docs\ga\cga-02-production-monitoring-incident-window.md'),
    (Join-Path $repo 'docs\ga\cga-02-monitoring-schedule.md'),
    (Join-Path $repo 'docs\ga\cga-02-incident-response-runbook.md'),
    (Join-Path $repo 'docs\ga\cga-02-evidence-matrix.md'),
    (Join-Path $repo 'docs\ga\cga-02-go-no-go.md')
)
foreach($doc in $docs){ Assert-True(Test-Path $doc) "Required CGA-02 document missing: $doc" }
Assert-DocumentContains $docs[0] @('CGA-01','CGA-02','CGA-03','PUBLIC GA: NOT ACTIVATED')
Assert-DocumentContains $docs[2] @('CGA-02','24h/72h','Production Monitoring','Incident Window','does not activate','publicGeneralAvailabilityActivated=False')
Assert-DocumentContains $docs[3] @('SampleCount','SampleIntervalSeconds','health/ready','observability','sync/status')
Assert-DocumentContains $docs[4] @('P0','P1','P2','rollback','waiting connections')
Assert-DocumentContains $docs[5] @('dashboard overview','sales range','schemaVersion 4','syncContract schema_version_4')
Assert-DocumentContains $docs[6] @('PASS','BLOCKED','NO-GO','PUBLIC GA: NOT ACTIVATED')
Write-Step 'Repository/document CGA-02 guardrails PASS'

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

if (-not $SkipCga01Revalidation.IsPresent) {
    Write-Step 'Revalidating CGA-01 prerequisite...'
    $cga01Args = @{ BaseUrl=$script:base; DashboardUrl=$DashboardUrl; TenantId=$TenantId; Email=$Email; Password=$Password; DatabaseUrl=$DatabaseUrl; RolloutMode='LIMITED'; MaxStores=$MaxStores; MaxConcurrentTerminals=$MaxConcurrentTerminals; ObservationWindowHours=24; SkipDashboardBuild=$SkipDashboardBuild; SkipPostGa12Revalidation=$true }
    & $cga01Script @cga01Args
    Write-Step 'CGA-01 prerequisite revalidation PASS'
} else {
    Write-Step 'CGA-01 prerequisite revalidation skipped by switch; CGA-02 requires existing CGA-01 PASS logs or runtime manifest.'
    if (Test-Path $cga01Manifest) {
        $cga01 = Get-Content -Raw $cga01Manifest | ConvertFrom-Json
        Assert-True(([string]$cga01.status).Contains('PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION')) 'CGA-01 manifest status is not PASS.'
        Assert-True([bool]$cga01.controlledRolloutAllowed) 'CGA-01 manifest does not allow controlled rollout.'
        Assert-True(-not [bool]$cga01.generalAvailabilityActivated) 'CGA-01 manifest indicates GA activated.'
    }
}

Write-Step 'Production monitoring API samples...'
$login = Invoke-Api Post '/api/v1/auth/login' @{ email = $Email; password = $plainPassword; tenantId = $TenantId }
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.accessToken)) 'Login access token missing'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$samples = @()
for ($i = 1; $i -le $SampleCount; $i++) {
    $now = [DateTimeOffset]::UtcNow
    $from = $now.AddDays(-7).ToString('o')
    $to = $now.ToString('o')
    $fromEsc = [uri]::EscapeDataString($from)
    $toEsc = [uri]::EscapeDataString($to)
    $liveStatus = Get-HttpStatus Get '/health/live'
    $readyStatus = Get-HttpStatus Get '/health/ready'
    $unauthObs = Get-HttpStatus Get '/api/v1/observability/metrics'
    $tenantStatus = Get-HttpStatus Get '/api/v1/tenants/current' $headers
    $salesRange = Invoke-Api Get ("/api/v1/reports/sales/range?from=$fromEsc&to=$toEsc") $null $headers
    $dashboardOverview = Invoke-Api Get ("/api/v1/reports/dashboard/overview?from=$fromEsc&to=$toEsc&limit=20&trendBucket=day") $null $headers
    $syncStatus = Invoke-Api Get '/api/v1/sync/status' $null $headers
    $syncContract = Invoke-Api Get '/api/v1/sync/contract' $null $headers
    $metrics = Invoke-Api Get '/api/v1/observability/metrics' $null $headers
    $dashStatus = 0
    if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) { $dashStatus = Get-HttpStatus Get $DashboardUrl @{} $null 30 -Absolute }
    Assert-True($liveStatus -eq 200) "sample $i health/live must return 200; status=$liveStatus"
    Assert-True($readyStatus -eq 200) "sample $i health/ready must return 200; status=$readyStatus"
    Assert-True($unauthObs -eq 401) "sample $i observability/metrics without auth must return 401; status=$unauthObs"
    Assert-2xx $tenantStatus "sample $i tenant-current"
    if ($dashStatus -ne 0) { Assert-True(($dashStatus -ge 200 -and $dashStatus -lt 400)) "sample $i DashboardUrl must return 2xx/3xx; status=$dashStatus" }
    Assert-True([int]$syncContract.currentSchemaVersion -eq 4) "sample $i sync contract currentSchemaVersion must remain 4."
    Assert-True(($dashboardOverview.PSObject.Properties['sales'] -ne $null)) "sample $i dashboard overview must include sales section."
    Assert-True([bool]$metrics.database.ready) "sample $i metrics database.ready must be true."
    Assert-True([long]$syncStatus.pendingCount -eq 0) "sample $i sync pendingCount must remain 0."
    Assert-True([long]$syncStatus.processingCount -eq 0) "sample $i sync processingCount must remain 0."
    Assert-True([long]$syncStatus.retryPendingCount -eq 0) "sample $i sync retryPendingCount must remain 0."
    Assert-True([long]$syncStatus.conflictCount -le $AllowedExistingSyncConflictCount) "sample $i sync conflictCount must be <= AllowedExistingSyncConflictCount=$AllowedExistingSyncConflictCount; actual=$($syncStatus.conflictCount)."
    $samples += [pscustomobject]@{
        sample = $i; sampledAt = $now.ToString('o'); healthLiveStatus = $liveStatus; healthReadyStatus = $readyStatus; unauthenticatedObservabilityStatus = $unauthObs; tenantCurrentStatus = $tenantStatus; dashboardUrlStatus = $dashStatus; salesRangeCompletedSalesCount = $salesRange.completedSalesCount; dashboardOverviewCompletedSalesCount = $dashboardOverview.sales.completedSalesCount; syncPendingCount = $syncStatus.pendingCount; syncProcessingCount = $syncStatus.processingCount; syncRetryPendingCount = $syncStatus.retryPendingCount; syncConflictCount = $syncStatus.conflictCount; syncDeadLetterCount = $syncStatus.deadLetterCount; metricsDatabaseReady = [bool]$metrics.database.ready; metricsP95LatencyMs = $metrics.requests.p95LatencyMs; metricsFailedRequests = $metrics.requests.failedRequests; syncContractCurrentSchemaVersion = [int]$syncContract.currentSchemaVersion
    }
    if ($i -lt $SampleCount -and $SampleIntervalSeconds -gt 0) { Start-Sleep -Seconds $SampleIntervalSeconds }
}
Write-Step 'Production monitoring API samples PASS'

Write-Step 'Database production monitoring snapshot...'
$sql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId; max_stores = $MaxStores; max_concurrent_terminals = $MaxConcurrentTerminals; monitoring_window_hours = $MonitoringWindowHours }
Assert-True([int]$sql.schemaVersion -eq 4) 'Database snapshot schemaVersion must be 4.'
Assert-True([string]$sql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must be schema_version_4.'
Assert-True([bool]$sql.requiredTablesPresent) "Missing required CGA-02 source tables: $($sql.missingRequiredTables -join ', ')"
Assert-True(-not [bool]$sql.generalAvailabilityActivated) 'CGA-02 SQL indicates General Availability already activated.'
Assert-True(-not [bool]$sql.publicGeneralAvailabilityActivated) 'CGA-02 SQL indicates Public General Availability already activated.'
Assert-True([long]$sql.tenantState.active_tenant_count -eq 1) 'Tenant must be active for CGA-02.'
Assert-True([long]$sql.rolloutScope.active_store_count -ge 1) 'At least one active store is required.'
Assert-True([long]$sql.rolloutScope.active_store_count -le $MaxStores) 'Active store count exceeds controlled rollout MaxStores.'
Assert-True([long]$sql.rolloutScope.open_shift_count -le $MaxConcurrentTerminals) 'Open shift count exceeds controlled rollout MaxConcurrentTerminals.'
Assert-True([long]$sql.syncIntegrity.legacy_schema_event_count -eq 0) 'Legacy schema event count must be 0.'
Assert-True([long]$sql.syncIntegrity.pending_conflict_count -le $AllowedExistingSyncConflictCount) "Pending conflict count must be <= AllowedExistingSyncConflictCount=$AllowedExistingSyncConflictCount."
Assert-True([long]$sql.syncIntegrity.retry_pending_count -eq 0) 'Retry pending sync count must be 0.'
Assert-True([long]$sql.syncIntegrity.stale_processing_count -eq 0) 'Stale processing count must be 0.'
Assert-True([long]$sql.financialIntegrity.duplicate_local_sale_count -eq 0) 'Duplicate local sale count must be 0.'
Assert-True([long]$sql.financialIntegrity.negative_payment_count -eq 0) 'Negative payment count must be 0.'
Assert-True([long]$sql.rls.rls_missing_table_count -eq 0) 'RLS missing table count must be 0.'
Assert-True([long]$sql.databasePressure.long_running_query_count -eq 0) 'Long running query count must be 0.'
Write-Step 'Database production monitoring snapshot PASS'

Write-Step 'CGA-02 incident window and blocker matrix...'
$blockers = @()
$conditions = @(
    'ga09_capacity_boundary_concurrency_3_plus_upstream_error_carried_forward',
    'ga10_ga11_ga12_postga12_db_waiting_connections_observation_carried_forward',
    'public_ga_activation_requires_explicit_separate_change',
    'dashboard_overview_requires_from_to_limit_trendBucket_contract',
    "monitoring_window_${MonitoringWindowHours}h_snapshot_gate"
)
if ($SkipDashboardBuild.IsPresent) { $conditions += 'dashboard_build_skipped' }
if ($SkipCga01Revalidation.IsPresent) { $conditions += 'cga01_revalidation_skipped_requires_external_cga01_pass_log' }
if ([long]$sql.databasePressure.waiting_connection_count -gt 0) { $conditions += "db_waiting_connections_$($sql.databasePressure.waiting_connection_count)" }
if ([long]$sql.syncIntegrity.dead_letter_count -gt 0) { $conditions += "historical_dead_letter_events_$($sql.syncIntegrity.dead_letter_count)" }
if ($AllowedExistingSyncConflictCount -gt 0) { $conditions += "known_sync_conflict_baseline_allowed_$AllowedExistingSyncConflictCount" }
if ([long]$sql.syncIntegrity.pending_conflict_count -gt 0) { $conditions += "pending_sync_conflicts_observed_$($sql.syncIntegrity.pending_conflict_count)" }
if (($null -ne $metrics.inventory) -and ([long]$metrics.inventory.lowStockItemCount -gt 0)) { $conditions += "low_stock_items_$($metrics.inventory.lowStockItemCount)" }
if ([long]$sql.rolloutScope.active_store_count -gt $MaxStores) { $blockers += 'controlled_rollout_store_scope_exceeded' }
if ([long]$sql.rolloutScope.open_shift_count -gt $MaxConcurrentTerminals) { $blockers += 'controlled_rollout_terminal_scope_exceeded' }
if ([long]$sql.syncIntegrity.legacy_schema_event_count -ne 0) { $blockers += 'legacy_schema_events' }
if ([long]$sql.syncIntegrity.pending_conflict_count -gt $AllowedExistingSyncConflictCount) { $blockers += 'pending_sync_conflicts_above_allowed_baseline' }
if ([long]$sql.syncIntegrity.retry_pending_count -ne 0) { $blockers += 'retry_pending_sync_events' }
if ([long]$sql.syncIntegrity.stale_processing_count -ne 0) { $blockers += 'stale_sync_processing' }
if ([long]$sql.financialIntegrity.duplicate_local_sale_count -ne 0) { $blockers += 'duplicate_local_sales' }
if ([long]$sql.financialIntegrity.negative_payment_count -ne 0) { $blockers += 'negative_payments' }
if ([long]$sql.rls.rls_missing_table_count -ne 0) { $blockers += 'rls_drift' }
if ([bool]$sql.generalAvailabilityActivated -eq $true) { $blockers += 'general_availability_activated_without_explicit_decision' }
if ([bool]$sql.publicGeneralAvailabilityActivated -eq $true) { $blockers += 'public_general_availability_activated_without_explicit_decision' }
Assert-True($blockers.Count -eq 0) "CGA-02 BLOCKED: $($blockers -join ', ')"
$status = 'PASS CGA-02 PRODUCTION MONITORING INCIDENT WINDOW / GO CGA-03'
Write-Step 'CGA-02 incident window and blocker matrix PASS'

$logoutStatus = Get-HttpStatus Post '/api/v1/auth/logout' $headers @{ refreshToken = [string]$login.refreshToken; tenantId = $TenantId }
Assert-True($logoutStatus -eq 204) "CGA-02 session logout must return 204; status=$logoutStatus"

$generated = (Get-Date).ToUniversalTime().ToString('o')
$lastSample = $samples[$samples.Count - 1]
$manifest = [ordered]@{
    phase = 'CGA-02'
    status = $status
    tenantId = $TenantId
    baseUrl = $script:base
    dashboardUrl = $DashboardUrl
    generatedAt = $generated
    validatorVersion = $script:Cga02ValidatorVersion
    entryGate = 'PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02'
    rolloutMode = $RolloutMode
    maxStores = $MaxStores
    maxConcurrentTerminals = $MaxConcurrentTerminals
    monitoringWindowHours = $MonitoringWindowHours
    sampleCount = $SampleCount
    sampleIntervalSeconds = $SampleIntervalSeconds
    allowedExistingSyncConflictCount = $AllowedExistingSyncConflictCount
    productionMonitoring = 'PASS'
    incidentWindow = 'PASS_NO_BLOCKING_INCIDENTS_DETECTED'
    controlledRolloutAllowed = $true
    launchAuthorizationOnly = $true
    publicGeneralAvailabilityActivated = $false
    generalAvailabilityActivated = $false
    knownCapacityCondition = 'GA-09 PASS at Concurrency 1/2; Concurrency 3+ current Railway/upstream path can return 400 upstream error.'
    knownDbCondition = 'GA-10/GA-11/GA-12/Post-GA-12/CGA-01 observed waiting connections; monitor/tune pool/connections before public GA launch.'
    dashboardOverviewContract = 'from,to,limit,trendBucket required; storeId optional.'
    healthLiveStatus = $lastSample.healthLiveStatus
    healthReadyStatus = $lastSample.healthReadyStatus
    unauthenticatedObservabilityStatus = $lastSample.unauthenticatedObservabilityStatus
    tenantCurrentStatus = $lastSample.tenantCurrentStatus
    salesRangeCompletedSalesCount = $lastSample.salesRangeCompletedSalesCount
    dashboardOverviewCompletedSalesCount = $lastSample.dashboardOverviewCompletedSalesCount
    syncPendingCount = $lastSample.syncPendingCount
    syncProcessingCount = $lastSample.syncProcessingCount
    syncRetryPendingCount = $lastSample.syncRetryPendingCount
    syncConflictCount = $lastSample.syncConflictCount
    syncDeadLetterCount = $lastSample.syncDeadLetterCount
    dashboardUrlStatus = $lastSample.dashboardUrlStatus
    dashboardBuild = $dashboardBuildCondition
    metricsDatabaseReady = $lastSample.metricsDatabaseReady
    metricsP95LatencyMs = $lastSample.metricsP95LatencyMs
    metricsFailedRequests = $lastSample.metricsFailedRequests
    syncContractCurrentSchemaVersion = $lastSample.syncContractCurrentSchemaVersion
    databaseSnapshot = $sql
    activeStoreCount = [long]$sql.rolloutScope.active_store_count
    availableTerminalCount = [long]$sql.rolloutScope.available_terminal_count
    openShiftCount = [long]$sql.rolloutScope.open_shift_count
    activeStableReleaseCount = [long]$sql.rolloutScope.active_stable_release_count
    completedSalesInMonitoringWindow = [long]$sql.monitoringActivity.completed_sales_in_window
    paymentsInMonitoringWindow = [long]$sql.monitoringActivity.payments_in_window
    receiptsIssuedInMonitoringWindow = [long]$sql.monitoringActivity.receipts_issued_in_window
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
    nextPhase = 'CGA-03 - Capacity / DB Remediation or Formal Acceptance'
}
$manifest | ConvertTo-Json -Depth 60 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{ manifest=$manifest; samples=$samples; databaseSnapshot=$sql; lastMetrics=$metrics; lastSyncStatus=$syncStatus; lastSalesRange=$salesRange; lastDashboardOverview=$dashboardOverview } | ConvertTo-Json -Depth 60 | Set-Content -Encoding UTF8 $snapshotPath
@"
# CGA-02 Production Monitoring and Incident Window Evidence

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Cga02ValidatorVersion
- rolloutMode: $RolloutMode
- monitoringWindowHours: $MonitoringWindowHours
- sampleCount: $SampleCount
- sampleIntervalSeconds: $SampleIntervalSeconds
- allowedExistingSyncConflictCount: $AllowedExistingSyncConflictCount
- publicGeneralAvailabilityActivated: False
- generalAvailabilityActivated: False
- incidentWindow: PASS_NO_BLOCKING_INCIDENTS_DETECTED
- healthLiveStatus: $($manifest.healthLiveStatus)
- healthReadyStatus: $($manifest.healthReadyStatus)
- dashboardUrlStatus: $($manifest.dashboardUrlStatus)
- dashboardOverviewContract: from,to,limit,trendBucket required; storeId optional
- syncContractCurrentSchemaVersion: $($manifest.syncContractCurrentSchemaVersion)
- activeStoreCount: $($manifest.activeStoreCount)
- openShiftCount: $($manifest.openShiftCount)
- completedSalesInMonitoringWindow: $($manifest.completedSalesInMonitoringWindow)
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
# CGA-02 Production Monitoring and Incident Window Log

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Cga02ValidatorVersion
- monitoringWindowHours: $MonitoringWindowHours
- sampleCount: $SampleCount
- allowedExistingSyncConflictCount: $AllowedExistingSyncConflictCount
- blockers: {}
- conditions: $($conditions -join ', ')
- publicGeneralAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath

$plainPassword = $null
[GC]::Collect()
Write-Step 'CGA-02 evidence manifest and production monitoring snapshot PASS'
Write-Step $status
[pscustomobject]$manifest
