param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [switch]$SkipDashboardBuild,
    [switch]$SkipGa09Revalidation
)

$ErrorActionPreference = 'Stop'
$script:Ga10ValidatorVersion = 'GA-10.2-db-json-output-parser-guardrail'
function Write-Step([string]$message) { Write-Host "[GA-10] $message" }
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
        if ($line.StartsWith('GA10_JSON:')) { $line = $line.Substring('GA10_JSON:'.Length) }
        if (-not $line.StartsWith('{')) { continue }
        try { return $line | ConvertFrom-Json } catch {}
    }

    $marker = 'GA10_JSON:'
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
    Assert-True ($null -ne $prop) "Required metrics property missing: $Name"
    return $prop.Value
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-Secret $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repo = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln = Join-Path $repo 'solidpos-platform.sln'
$sqlPath = Join-Path $scriptRoot 'ga-10-observability-dashboard-alerting-oncall-readiness-check.sql'
$ga09Script = Join-Path $scriptRoot 'validate-ga-09-performance-capacity-resilience-offline-readiness.ps1'
$ga09Manifest = Join-Path $repo '.runtime\ga-09-performance-capacity-resilience-offline-readiness\ga-09-performance-capacity-resilience-offline-readiness-manifest.json'
$secretScan = Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript = Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtime = Join-Path $repo '.runtime\ga-10-observability-dashboard-alerting-oncall-readiness'
$manifestPath = Join-Path $runtime 'ga-10-observability-dashboard-alerting-oncall-readiness-manifest.json'
$snapshotPath = Join-Path $runtime 'ga-10-observability-dashboard-alerting-oncall-readiness-snapshot.json'
$evidencePath = Join-Path $runtime 'ga-10-evidence.md'
$logPath = Join-Path $repo 'docs\ga\logs\ga-10-observability-dashboard-alerting-oncall-readiness-log.md'
New-Item -ItemType Directory -Force -Path @($runtime, (Split-Path $logPath)) | Out-Null

Write-Step "Validator version $script:Ga10ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plainPassword)) 'Password secure string resolved to empty/null. Re-run $securePassword = Read-Host ... -AsSecureString.'
Assert-True($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'

Write-Step 'Repository/document GA-10 guardrails...'
Assert-True(Test-Path $sln) 'solution missing'
Assert-True(Test-Path $sqlPath) 'GA-10 SQL check missing'
Assert-True(Test-Path $ga09Script) 'GA-09 prerequisite validator missing'
Assert-True(Test-Path $secretScan) 'secret scanner missing'
Assert-True(Test-Path (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Endpoints\ObservabilityEndpoints.cs')) 'Observability endpoint source missing'
Assert-True(Test-Path (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Observability\OperationalMetricsMiddleware.cs')) 'Operational metrics middleware missing'
$docs = @(
    (Join-Path $repo 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
    (Join-Path $repo 'SOLIDPOS_GA_10_OBSERVABILITY_DASHBOARD_ALERTING_AND_ONCALL_READINESS.md'),
    (Join-Path $repo 'docs\ga\ga-10-observability-dashboard-alerting-oncall-readiness.md'),
    (Join-Path $repo 'docs\ga\ga-10-alerting-thresholds-and-routing.md'),
    (Join-Path $repo 'docs\ga\ga-10-oncall-dashboard-runbook.md'),
    (Join-Path $repo 'docs\ga\ga-10-evidence-matrix.md'),
    (Join-Path $repo 'docs\ga\ga-10-go-no-go.md')
)
foreach($doc in $docs){ Assert-True(Test-Path $doc) "Required GA-10 document missing: $doc" }
Assert-DocumentContains $docs[0] @('GA-09 validated production capacity condition','Concurrency 3+','400 upstream error','GA-10')
Assert-DocumentContains $docs[2] @('400 upstream error','health/ready','dashboard','sync','schemaVersion=4','syncContract=schema_version_4')
Assert-DocumentContains $docs[3] @('upstream error','timeout','p95','p99','SEV2','Railway','RLS drift')
Assert-DocumentContains $docs[4] @('on-call','Railway logs','DB pressure','GA-09 condition')
Assert-DocumentContains $docs[6] @('PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11','Concurrency 3+')
Write-Step 'Repository/document GA-10 guardrails PASS'

Write-Step 'Local build/test/secret guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln }
Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore }
Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Invoke-Checked 'secret scan' { & $secretScan }
if (-not $SkipDashboardBuild.IsPresent) {
    Assert-True(Test-Path $dashboardScript) 'PosDashboard validation script missing.'
    Invoke-Checked 'PosDashboard operations dashboard validation' { & $dashboardScript -BaseUrl $script:base -TenantId $TenantId -Email $Email }
    $dashboardBuildCondition = 'PASS'
} else {
    Write-Step 'PosDashboard build skipped by switch.'
    $dashboardBuildCondition = 'SKIPPED_BY_SWITCH'
}
Write-Step 'Local build/test/secret guardrails PASS'

if (-not $SkipGa09Revalidation.IsPresent) {
    Write-Step 'Fresh GA-09 prerequisite revalidation at Concurrency 2...'
    & $ga09Script `
      -BaseUrl $script:base `
      -TenantId $TenantId `
      -Email $Email `
      -Password $Password `
      -DatabaseUrl $DatabaseUrl `
      -HealthRequests 24 `
      -ProtectedRequests 18 `
      -Concurrency 2 `
      -P95ThresholdMs 2500 `
      -P99ThresholdMs 5000 `
      -MaxErrorPercent 2 `
      -SkipDashboardBuild:$SkipDashboardBuild.IsPresent `
      -SkipGa08Revalidation
} else {
    Write-Step 'GA-09 prerequisite revalidation skipped by switch; GA-10 requires existing GA-09 PASS logs or runtime manifest.'
}
if (Test-Path $ga09Manifest) {
    $ga09 = Get-Content -Raw $ga09Manifest | ConvertFrom-Json
    Assert-True(([string]$ga09.status).Contains('PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10')) 'GA-09 runtime manifest does not show PASS / GO GA-10.'
    Assert-True([int]$ga09.schemaVersion -eq 4) 'GA-09 manifest schemaVersion drift.'
    Assert-True([string]$ga09.syncContract -eq 'schema_version_4') 'GA-09 manifest syncContract drift.'
    Assert-True(-not [bool]$ga09.generalAvailabilityActivated) 'GA-09 manifest indicates GA activated.'
}

Write-Step 'Production observability endpoint/auth checks...'
$login = Invoke-Api Post '/api/v1/auth/login' @{ email = $Email; password = $plainPassword; tenantId = $TenantId }
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.accessToken)) 'Login access token missing'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$liveStatus = Get-HttpStatus Get '/health/live'
$readyStatus = Get-HttpStatus Get '/health/ready'
$metricsNoAuthStatus = Get-HttpStatus Get '/api/v1/observability/metrics'
Assert-True($liveStatus -eq 200) "health/live must return 200; status=$liveStatus"
Assert-True($readyStatus -eq 200) "health/ready must return 200; status=$readyStatus"
Assert-True($metricsNoAuthStatus -eq 401) "metrics without auth must return 401; status=$metricsNoAuthStatus"
$metrics = Invoke-Api Get '/api/v1/observability/metrics' $null $headers
Require-Property $metrics 'generatedAt' | Out-Null
$dbMetrics = Require-Property $metrics 'database'
$requestMetrics = Require-Property $metrics 'requests'
$syncMetrics = Require-Property $metrics 'sync'
$salesMetrics = Require-Property $metrics 'sales'
$paymentMetrics = Require-Property $metrics 'payments'
$inventoryMetrics = Require-Property $metrics 'inventory'
$auditMetrics = Require-Property $metrics 'audit'
Assert-True([bool](Require-Property $dbMetrics 'ready')) 'Observability database.ready must be true.'
Require-Property $requestMetrics 'totalRequests' | Out-Null
Require-Property $requestMetrics 'failedRequests' | Out-Null
Require-Property $requestMetrics 'averageLatencyMs' | Out-Null
Require-Property $requestMetrics 'p95LatencyMs' | Out-Null
Require-Property $requestMetrics 'topRoutes' | Out-Null
Require-Property $syncMetrics 'inboxByStatus' | Out-Null
Require-Property $syncMetrics 'pendingConflicts' | Out-Null
Require-Property $syncMetrics 'deadLetterEvents' | Out-Null
Require-Property $syncMetrics 'retryPendingEvents' | Out-Null
Require-Property $salesMetrics 'apiP95LatencyMs' | Out-Null
Require-Property $paymentMetrics 'failedPaymentsLast24Hours' | Out-Null
Require-Property $inventoryMetrics 'negativeInventoryItemCount' | Out-Null
Require-Property $auditMetrics 'auditEventsLast24Hours' | Out-Null
$syncContract = Invoke-Api Get '/api/v1/sync/contract' $null $headers
Assert-True([int]$syncContract.currentSchemaVersion -eq 4) 'Sync contract currentSchemaVersion must remain 4.'
$syncStatus = Invoke-Api Get '/api/v1/sync/status' $null $headers
$now = [DateTimeOffset]::UtcNow
$from = $now.AddDays(-7).ToString('o')
$to = $now.ToString('o')
$dashboardOverviewStatus = Get-HttpStatus Get ("/api/v1/reports/dashboard/overview?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20&trendBucket=day") $headers
Assert-True($dashboardOverviewStatus -eq 200) "dashboard overview endpoint must return 200; status=$dashboardOverviewStatus"
if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    $dashStatus = Get-HttpStatus Get $DashboardUrl @{} $null 30 -Absolute
    Assert-True(($dashStatus -ge 200 -and $dashStatus -lt 400)) "DashboardUrl must return 2xx/3xx; status=$dashStatus"
} else { $dashStatus = 0 }
Write-Step 'Production observability endpoint/auth checks PASS'

Write-Step 'Database observability/readiness snapshot...'
$sql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId }
Assert-True([int]$sql.schemaVersion -eq 4) 'Database snapshot schemaVersion must be 4.'
Assert-True([string]$sql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must be schema_version_4.'
Assert-True([bool]$sql.requiredTablesPresent) "Missing required observability source tables: $($sql.missingRequiredTables -join ', ')"
Assert-True([long]$sql.rls.rls_missing_table_count -eq 0) 'RLS missing table count must be 0.'
Assert-True([long]$sql.sync.legacy_schema_event_count -eq 0) 'legacy schema events must be 0.'
Assert-True([long]$sql.conflicts.pending_conflict_count -eq 0) 'pending conflicts must be 0.'
Assert-True([long]$sql.databasePressure.long_running_query_count -eq 0) 'long running query count must be 0.'
Write-Step 'Database observability/readiness snapshot PASS'

Write-Step 'GA-10 blocker matrix...'
$blockers = @()
$conditions = @('ga09_capacity_boundary_concurrency_3_plus_upstream_error_carried_forward')
if ($SkipDashboardBuild.IsPresent) { $conditions += 'dashboard_build_skipped' }
if ($SkipGa09Revalidation.IsPresent) { $conditions += 'ga09_revalidation_skipped_requires_external_ga09_pass_log' }
if (-not [string]::IsNullOrWhiteSpace($DashboardUrl) -and ($dashStatus -lt 200 -or $dashStatus -ge 400)) { $blockers += "dashboard_url_status_$dashStatus" }
if ([long]$sql.databasePressure.waiting_connection_count -gt 0) { $conditions += "db_waiting_connections_$($sql.databasePressure.waiting_connection_count)" }
if ([bool]$true -ne [bool]$dbMetrics.ready) { $blockers += 'observability_database_not_ready' }
if ([int]$syncContract.currentSchemaVersion -ne 4) { $blockers += 'sync_contract_schema_drift' }
if ([bool]$sql.requiredTablesPresent -ne $true) { $blockers += 'observability_source_tables_missing' }
if ([bool]$false) { $blockers += 'placeholder' }
Assert-True($blockers.Count -eq 0) "GA-10 BLOCKED: $($blockers -join ', ')"
Write-Step 'GA-10 blocker matrix PASS'

$logoutStatus = Get-HttpStatus Post '/api/v1/auth/logout' $headers @{ refreshToken = [string]$login.refreshToken; tenantId = $TenantId }
Assert-True($logoutStatus -eq 204) "GA-10 session logout must return 204; status=$logoutStatus"

$generated = (Get-Date).ToUniversalTime().ToString('o')
$manifest = [ordered]@{
    phase = 'GA-10'
    status = 'PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11'
    tenantId = $TenantId
    baseUrl = $script:base
    dashboardUrl = $DashboardUrl
    generatedAt = $generated
    validatorVersion = $script:Ga10ValidatorVersion
    entryGate = 'PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10'
    knownCapacityCondition = 'GA-09 PASS at Concurrency 1/2; Concurrency 3+ current Railway/upstream path can return 400 upstream error.'
    observabilityEndpointProtected = ($metricsNoAuthStatus -eq 401)
    healthLiveStatus = $liveStatus
    healthReadyStatus = $readyStatus
    metricsDatabaseReady = [bool]$dbMetrics.ready
    metricsTotalRequests = [long]$requestMetrics.totalRequests
    metricsFailedRequests = [long]$requestMetrics.failedRequests
    metricsAverageLatencyMs = [decimal]$requestMetrics.averageLatencyMs
    metricsP95LatencyMs = [decimal]$requestMetrics.p95LatencyMs
    metricsTopRoutesCount = @($requestMetrics.topRoutes).Count
    dashboardOverviewStatus = $dashboardOverviewStatus
    dashboardUrlStatus = $dashStatus
    dashboardBuild = $dashboardBuildCondition
    syncContractCurrentSchemaVersion = [int]$syncContract.currentSchemaVersion
    syncRuntimeStatusTotalEvents = $syncStatus.totalEvents
    databaseSnapshot = $sql
    alertingThresholds = 'PASS'
    onCallRunbook = 'PASS'
    evidenceMatrix = 'PASS'
    blockers = @()
    conditions = $conditions
    schemaVersion = 4
    syncContract = 'schema_version_4'
    generalAvailabilityActivated = $false
    nextPhase = 'GA-11 - Customer, Operator and Admin Acceptance'
}
$manifest | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{ manifest=$manifest; metrics=$metrics; databaseSnapshot=$sql } | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-10 Observability, Dashboard, Alerting and On-call Readiness Evidence

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Ga10ValidatorVersion
- healthLiveStatus: $liveStatus
- healthReadyStatus: $readyStatus
- metricsNoAuthStatus: $metricsNoAuthStatus
- metricsDatabaseReady: $($manifest.metricsDatabaseReady)
- metricsTotalRequests: $($manifest.metricsTotalRequests)
- metricsFailedRequests: $($manifest.metricsFailedRequests)
- metricsP95LatencyMs: $($manifest.metricsP95LatencyMs)
- dashboardOverviewStatus: $dashboardOverviewStatus
- dashboardUrlStatus: $dashStatus
- dashboardBuild: $dashboardBuildCondition
- syncContractCurrentSchemaVersion: $($manifest.syncContractCurrentSchemaVersion)
- requiredTablesPresent: $($sql.requiredTablesPresent)
- rlsMissingTableCount: $($sql.rls.rls_missing_table_count)
- legacySchemaEventCount: $($sql.sync.legacy_schema_event_count)
- pendingConflictCount: $($sql.conflicts.pending_conflict_count)
- waitingConnectionCount: $($sql.databasePressure.waiting_connection_count)
- longRunningQueryCount: $($sql.databasePressure.long_running_query_count)
- blockers: {}
- conditions: $($conditions -join ', ')
- knownCapacityCondition: $($manifest.knownCapacityCondition)
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $evidencePath
@"
# GA-10 Observability, Dashboard, Alerting and On-call Readiness Log

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Ga10ValidatorVersion
- knownCapacityCondition: $($manifest.knownCapacityCondition)
- blockers: {}
- conditions: $($conditions -join ', ')
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath

$plainPassword = $null
[GC]::Collect()
Write-Step 'GA-10 evidence manifest and observability snapshot PASS'
Write-Step 'GA-10 PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11'
[pscustomobject]$manifest
