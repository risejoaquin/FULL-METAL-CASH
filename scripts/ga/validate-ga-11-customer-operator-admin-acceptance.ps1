param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [switch]$SkipDashboardBuild,
    [switch]$SkipGa10Revalidation
)

$ErrorActionPreference = 'Stop'
$script:Ga11ValidatorVersion = 'GA-11.0-customer-operator-admin-acceptance'
function Write-Step([string]$message) { Write-Host "[GA-11] $message" }
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
        if ($line.StartsWith('GA11_JSON:')) { $line = $line.Substring('GA11_JSON:'.Length) }
        if (-not $line.StartsWith('{')) { continue }
        try { return $line | ConvertFrom-Json } catch {}
    }
    $marker = 'GA11_JSON:'
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
function Count-Items($value) { if ($null -eq $value) { return 0 }; return @($value).Count }

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-Secret $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repo = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln = Join-Path $repo 'solidpos-platform.sln'
$sqlPath = Join-Path $scriptRoot 'ga-11-customer-operator-admin-acceptance-check.sql'
$ga10Script = Join-Path $scriptRoot 'validate-ga-10-observability-dashboard-alerting-oncall-readiness.ps1'
$ga10Manifest = Join-Path $repo '.runtime\ga-10-observability-dashboard-alerting-oncall-readiness\ga-10-observability-dashboard-alerting-oncall-readiness-manifest.json'
$secretScan = Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript = Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtime = Join-Path $repo '.runtime\ga-11-customer-operator-admin-acceptance'
$manifestPath = Join-Path $runtime 'ga-11-customer-operator-admin-acceptance-manifest.json'
$snapshotPath = Join-Path $runtime 'ga-11-customer-operator-admin-acceptance-snapshot.json'
$evidencePath = Join-Path $runtime 'ga-11-evidence.md'
$logPath = Join-Path $repo 'docs\ga\logs\ga-11-customer-operator-admin-acceptance-log.md'
New-Item -ItemType Directory -Force -Path @($runtime, (Split-Path $logPath)) | Out-Null

Write-Step "Validator version $script:Ga11ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plainPassword)) 'Password secure string resolved to empty/null. Re-run $securePassword = Read-Host ... -AsSecureString.'
Assert-True($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'

Write-Step 'Repository/document GA-11 guardrails...'
Assert-True(Test-Path $sln) 'solution missing'
Assert-True(Test-Path $sqlPath) 'GA-11 SQL check missing'
Assert-True(Test-Path $ga10Script) 'GA-10 prerequisite validator missing'
Assert-True(Test-Path $secretScan) 'secret scanner missing'
$docs = @(
    (Join-Path $repo 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
    (Join-Path $repo 'SOLIDPOS_GA_11_CUSTOMER_OPERATOR_ADMIN_ACCEPTANCE.md'),
    (Join-Path $repo 'docs\ga\ga-11-customer-operator-admin-acceptance.md'),
    (Join-Path $repo 'docs\ga\ga-11-customer-acceptance-checklist.md'),
    (Join-Path $repo 'docs\ga\ga-11-operator-acceptance-checklist.md'),
    (Join-Path $repo 'docs\ga\ga-11-admin-acceptance-checklist.md'),
    (Join-Path $repo 'docs\ga\ga-11-evidence-matrix.md'),
    (Join-Path $repo 'docs\ga\ga-11-go-no-go.md')
)
foreach($doc in $docs){ Assert-True(Test-Path $doc) "Required GA-11 document missing: $doc" }
Assert-DocumentContains $docs[0] @('GA-10','PASS REAL PRODUCTION / GO GA-11','GA-11','NEXT','Concurrency 3+','db_waiting_connections_11')
Assert-DocumentContains $docs[2] @('Customer, Operator and Admin Acceptance','customer acceptance','operator acceptance','admin acceptance','schemaVersion=4','syncContract=schema_version_4')
Assert-DocumentContains $docs[3] @('customer acceptance','receipt','returns','support','known capacity condition')
Assert-DocumentContains $docs[4] @('operator acceptance','sales','cash shift','offline','sync')
Assert-DocumentContains $docs[5] @('admin acceptance','users','roles','dashboard','observability')
Assert-DocumentContains $docs[7] @('PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12','Concurrency 3+','db_waiting_connections_11')
Write-Step 'Repository/document GA-11 guardrails PASS'

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

if (-not $SkipGa10Revalidation.IsPresent) {
    Write-Step 'Fresh GA-10 prerequisite revalidation...'
    & $ga10Script `
      -BaseUrl $script:base `
      -DashboardUrl $DashboardUrl `
      -TenantId $TenantId `
      -Email $Email `
      -Password $Password `
      -DatabaseUrl $DatabaseUrl `
      -SkipDashboardBuild:$SkipDashboardBuild.IsPresent `
      -SkipGa09Revalidation
} else {
    Write-Step 'GA-10 prerequisite revalidation skipped by switch; GA-11 requires existing GA-10 PASS logs or runtime manifest.'
}
if (Test-Path $ga10Manifest) {
    $ga10 = Get-Content -Raw $ga10Manifest | ConvertFrom-Json
    Assert-True(([string]$ga10.status).Contains('PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11')) 'GA-10 runtime manifest does not show PASS / GO GA-11.'
    Assert-True([int]$ga10.schemaVersion -eq 4) 'GA-10 manifest schemaVersion drift.'
    Assert-True([string]$ga10.syncContract -eq 'schema_version_4') 'GA-10 manifest syncContract drift.'
    Assert-True(-not [bool]$ga10.generalAvailabilityActivated) 'GA-10 manifest indicates GA activated.'
}

Write-Step 'Production customer/operator/admin acceptance API checks...'
$login = Invoke-Api Post '/api/v1/auth/login' @{ email = $Email; password = $plainPassword; tenantId = $TenantId }
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.accessToken)) 'Login access token missing'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$now = [DateTimeOffset]::UtcNow
$from = $now.AddDays(-7).ToString('o')
$to = $now.ToString('o')
$liveStatus = Get-HttpStatus Get '/health/live'
$readyStatus = Get-HttpStatus Get '/health/ready'
$unauthStatus = Get-HttpStatus Get '/api/v1/tenants/current'
$tenantStatus = Get-HttpStatus Get '/api/v1/tenants/current' $headers
$storesStatus = Get-HttpStatus Get '/api/v1/stores' $headers
$usersStatus = Get-HttpStatus Get '/api/v1/users' $headers
$rolesStatus = Get-HttpStatus Get '/api/v1/roles' $headers
$permissionsStatus = Get-HttpStatus Get '/api/v1/permissions' $headers
$catalogStatus = Get-HttpStatus Get '/api/v1/tenant/catalog' $headers
$customersStatus = Get-HttpStatus Get '/api/v1/customers?limit=20' $headers
$salesStatus = Get-HttpStatus Get ("/api/v1/sales?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20") $headers
$dashboardOverviewStatus = Get-HttpStatus Get ("/api/v1/reports/dashboard/overview?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20&trendBucket=day") $headers
$salesRangeStatus = Get-HttpStatus Get ("/api/v1/reports/sales/range?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))") $headers
$inventoryStockStatus = Get-HttpStatus Get '/api/v1/inventory/stock' $headers
$syncStatusCode = Get-HttpStatus Get '/api/v1/sync/status' $headers
$syncContract = Invoke-Api Get '/api/v1/sync/contract' $null $headers
$metrics = Invoke-Api Get '/api/v1/observability/metrics' $null $headers
if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    $dashStatus = Get-HttpStatus Get $DashboardUrl @{} $null 30 -Absolute
    Assert-True(($dashStatus -ge 200 -and $dashStatus -lt 400)) "DashboardUrl must return 2xx/3xx; status=$dashStatus"
} else { $dashStatus = 0 }
Assert-True($liveStatus -eq 200) "health/live must return 200; status=$liveStatus"
Assert-True($readyStatus -eq 200) "health/ready must return 200; status=$readyStatus"
Assert-True($unauthStatus -eq 401) "tenant/current without auth must return 401; status=$unauthStatus"
foreach($pair in @(
    @($tenantStatus,'tenant-current'),@($storesStatus,'stores'),@($usersStatus,'users'),@($rolesStatus,'roles'),@($permissionsStatus,'permissions'),
    @($catalogStatus,'tenant-catalog'),@($customersStatus,'customers'),@($salesStatus,'sales-list'),@($dashboardOverviewStatus,'dashboard-overview'),
    @($salesRangeStatus,'sales-range'),@($inventoryStockStatus,'inventory-stock'),@($syncStatusCode,'sync-status')
)) { Assert-2xx ([int]$pair[0]) ([string]$pair[1]) }
Assert-True([int]$syncContract.currentSchemaVersion -eq 4) 'Sync contract currentSchemaVersion must remain 4.'
$dbMetrics = Require-Property $metrics 'database'
Assert-True([bool](Require-Property $dbMetrics 'ready')) 'Observability database.ready must be true.'
Write-Step 'Production customer/operator/admin acceptance API checks PASS'

Write-Step 'Database customer/operator/admin acceptance snapshot...'
$sql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId }
Assert-True([int]$sql.schemaVersion -eq 4) 'Database snapshot schemaVersion must be 4.'
Assert-True([string]$sql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must be schema_version_4.'
Assert-True([bool]$sql.requiredTablesPresent) "Missing required acceptance source tables: $($sql.missingRequiredTables -join ', ')"
Assert-True([long]$sql.tenantState.active_tenant_count -eq 1) 'Tenant must be active for GA-11 acceptance.'
Assert-True([long]$sql.operatorReadiness.active_store_count -gt 0) 'At least one active store is required for operator acceptance.'
Assert-True([long]$sql.operatorReadiness.available_terminal_count -gt 0) 'At least one available terminal is required for operator acceptance.'
Assert-True([long]$sql.operatorReadiness.active_sellable_product_count -gt 0) 'At least one active sellable product is required for operator acceptance.'
Assert-True([long]$sql.operatorReadiness.active_payment_method_count -gt 0) 'At least one active payment method is required for operator acceptance.'
Assert-True([long]$sql.adminReadiness.active_user_count -gt 0) 'At least one active user is required for admin acceptance.'
Assert-True([long]$sql.adminReadiness.role_count -gt 0) 'At least one role is required for admin acceptance.'
Assert-True([long]$sql.adminReadiness.permission_count -gt 0) 'Permissions are required for admin acceptance.'
Assert-True([long]$sql.adminReadiness.user_role_assignment_count -gt 0) 'At least one user role assignment is required.'
Assert-True([long]$sql.integrity.duplicate_local_sale_count -eq 0) 'Duplicate local sale count must be 0.'
Assert-True([long]$sql.integrity.legacy_schema_event_count -eq 0) 'Legacy schema event count must be 0.'
Assert-True([long]$sql.integrity.pending_conflict_count -eq 0) 'Pending conflict count must be 0.'
Assert-True([long]$sql.integrity.negative_payment_count -eq 0) 'Negative payment count must be 0.'
Assert-True([long]$sql.rls.rls_missing_table_count -eq 0) 'RLS missing table count must be 0.'
Assert-True([long]$sql.databasePressure.long_running_query_count -eq 0) 'Long running query count must be 0.'
Write-Step 'Database customer/operator/admin acceptance snapshot PASS'

Write-Step 'GA-11 blocker matrix...'
$blockers = @()
$conditions = @(
    'ga09_capacity_boundary_concurrency_3_plus_upstream_error_carried_forward',
    'ga10_db_waiting_connections_observation_carried_forward'
)
if ($SkipDashboardBuild.IsPresent) { $conditions += 'dashboard_build_skipped' }
if ($SkipGa10Revalidation.IsPresent) { $conditions += 'ga10_revalidation_skipped_requires_external_ga10_pass_log' }
if ([long]$sql.databasePressure.waiting_connection_count -gt 0) { $conditions += "db_waiting_connections_$($sql.databasePressure.waiting_connection_count)" }
if ([long]$sql.operatorReadiness.open_shift_count -gt 0) { $conditions += "open_shift_count_$($sql.operatorReadiness.open_shift_count)_requires_launch_attention" }
if ([long]$sql.customerReadiness.completed_sale_count -eq 0) { $conditions += 'no_completed_sales_in_acceptance_snapshot' }
if ([long]$sql.customerReadiness.active_digital_receipt_count -eq 0) { $conditions += 'no_active_digital_receipts_in_acceptance_snapshot' }
if ([long]$sql.customerReadiness.customer_count -eq 0) { $conditions += 'no_customers_in_acceptance_snapshot' }
if ([int]$syncContract.currentSchemaVersion -ne 4) { $blockers += 'sync_contract_schema_drift' }
if ([bool]$sql.requiredTablesPresent -ne $true) { $blockers += 'acceptance_source_tables_missing' }
if ([long]$sql.integrity.duplicate_local_sale_count -ne 0) { $blockers += 'duplicate_local_sales' }
if ([long]$sql.integrity.pending_conflict_count -ne 0) { $blockers += 'pending_sync_conflicts' }
if ([long]$sql.rls.rls_missing_table_count -ne 0) { $blockers += 'rls_drift' }
Assert-True($blockers.Count -eq 0) "GA-11 BLOCKED: $($blockers -join ', ')"
Write-Step 'GA-11 blocker matrix PASS'

$logoutStatus = Get-HttpStatus Post '/api/v1/auth/logout' $headers @{ refreshToken = [string]$login.refreshToken; tenantId = $TenantId }
Assert-True($logoutStatus -eq 204) "GA-11 session logout must return 204; status=$logoutStatus"

$generated = (Get-Date).ToUniversalTime().ToString('o')
$manifest = [ordered]@{
    phase = 'GA-11'
    status = 'PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12'
    tenantId = $TenantId
    baseUrl = $script:base
    dashboardUrl = $DashboardUrl
    generatedAt = $generated
    validatorVersion = $script:Ga11ValidatorVersion
    entryGate = 'PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11'
    knownCapacityCondition = 'GA-09 PASS at Concurrency 1/2; Concurrency 3+ current Railway/upstream path can return 400 upstream error.'
    knownDbCondition = 'GA-10 observed db_waiting_connections_11; monitor pool/connections before GA public launch.'
    customerAcceptance = 'PASS'
    operatorAcceptance = 'PASS'
    adminAcceptance = 'PASS'
    healthLiveStatus = $liveStatus
    healthReadyStatus = $readyStatus
    unauthenticatedTenantCurrentStatus = $unauthStatus
    tenantCurrentStatus = $tenantStatus
    storesStatus = $storesStatus
    usersStatus = $usersStatus
    rolesStatus = $rolesStatus
    permissionsStatus = $permissionsStatus
    catalogStatus = $catalogStatus
    customersStatus = $customersStatus
    salesListStatus = $salesStatus
    dashboardOverviewStatus = $dashboardOverviewStatus
    salesRangeStatus = $salesRangeStatus
    inventoryStockStatus = $inventoryStockStatus
    syncStatusCode = $syncStatusCode
    dashboardUrlStatus = $dashStatus
    dashboardBuild = $dashboardBuildCondition
    syncContractCurrentSchemaVersion = [int]$syncContract.currentSchemaVersion
    databaseSnapshot = $sql
    activeStoreCount = [long]$sql.operatorReadiness.active_store_count
    availableTerminalCount = [long]$sql.operatorReadiness.available_terminal_count
    activeSellableProductCount = [long]$sql.operatorReadiness.active_sellable_product_count
    activePaymentMethodCount = [long]$sql.operatorReadiness.active_payment_method_count
    activeUserCount = [long]$sql.adminReadiness.active_user_count
    roleCount = [long]$sql.adminReadiness.role_count
    permissionCount = [long]$sql.adminReadiness.permission_count
    duplicateLocalSaleCount = [long]$sql.integrity.duplicate_local_sale_count
    legacySchemaEventCount = [long]$sql.integrity.legacy_schema_event_count
    pendingConflictCount = [long]$sql.integrity.pending_conflict_count
    rlsMissingTableCount = [long]$sql.rls.rls_missing_table_count
    waitingConnectionCount = [long]$sql.databasePressure.waiting_connection_count
    longRunningQueryCount = [long]$sql.databasePressure.long_running_query_count
    blockers = @()
    conditions = $conditions
    schemaVersion = 4
    syncContract = 'schema_version_4'
    generalAvailabilityActivated = $false
    nextPhase = 'GA-12 - Final General Availability Launch Readiness'
}
$manifest | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{ manifest=$manifest; databaseSnapshot=$sql; metrics=$metrics } | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-11 Customer, Operator and Admin Acceptance Evidence

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Ga11ValidatorVersion
- customerAcceptance: PASS
- operatorAcceptance: PASS
- adminAcceptance: PASS
- healthLiveStatus: $liveStatus
- healthReadyStatus: $readyStatus
- tenantCurrentStatus: $tenantStatus
- storesStatus: $storesStatus
- usersStatus: $usersStatus
- rolesStatus: $rolesStatus
- permissionsStatus: $permissionsStatus
- catalogStatus: $catalogStatus
- customersStatus: $customersStatus
- salesListStatus: $salesStatus
- dashboardOverviewStatus: $dashboardOverviewStatus
- inventoryStockStatus: $inventoryStockStatus
- syncStatusCode: $syncStatusCode
- syncContractCurrentSchemaVersion: $($manifest.syncContractCurrentSchemaVersion)
- activeStoreCount: $($manifest.activeStoreCount)
- availableTerminalCount: $($manifest.availableTerminalCount)
- activeSellableProductCount: $($manifest.activeSellableProductCount)
- activePaymentMethodCount: $($manifest.activePaymentMethodCount)
- activeUserCount: $($manifest.activeUserCount)
- duplicateLocalSaleCount: $($manifest.duplicateLocalSaleCount)
- legacySchemaEventCount: $($manifest.legacySchemaEventCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
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
# GA-11 Customer, Operator and Admin Acceptance Log

- status: $($manifest.status)
- generatedAt: $generated
- validatorVersion: $script:Ga11ValidatorVersion
- blockers: {}
- conditions: $($conditions -join ', ')
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath

$plainPassword = $null
[GC]::Collect()
Write-Step 'GA-11 evidence manifest and acceptance snapshot PASS'
Write-Step 'GA-11 PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12'
[pscustomobject]$manifest
