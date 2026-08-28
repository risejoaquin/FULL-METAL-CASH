param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-07] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString {
    param([securestring]$SecureValue)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Get-Items {
    param($Response)
    if ($null -eq $Response) { return @() }
    if ($Response -is [System.Array]) { return @($Response) }
    if ($null -ne $Response.items) { return @($Response.items) }
    if ($null -ne $Response.data) { return @($Response.data) }
    if ($null -ne $Response.results) { return @($Response.results) }
    if ($null -ne $Response.events) { return @($Response.events) }
    if ($null -ne $Response.conflicts) { return @($Response.conflicts) }
    if ($null -ne $Response.sales) { return @($Response.sales) }
    return @($Response)
}
function Get-LongValue {
    param($Object, [string[]]$Names, [long]$Default = 0)
    if ($null -eq $Object) { return $Default }
    foreach ($name in $Names) {
        if ($null -ne $Object.$name) { return [long]$Object.$name }
    }
    return $Default
}
function Invoke-DbJsonFile {
    param(
        [Parameter(Mandatory = $true)] [string]$SqlPath,
        [Parameter(Mandatory = $true)] [hashtable]$Variables
    )
    $mountDirectory = (Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $fileName = Split-Path -Leaf $SqlPath
    $args = @("run", "--rm", "--env", "DATABASE_URL=$DatabaseUrl", "-v", "${mountDirectory}:/sql:ro", "postgres:16", "psql", "$DatabaseUrl", "-tA", "-v", "ON_ERROR_STOP=1")
    foreach ($key in $Variables.Keys) { $args += @("-v", "$key=$($Variables[$key])") }
    $args += @("-f", "/sql/$fileName")
    $global:LASTEXITCODE = 0
    $output = docker @args
    if ($LASTEXITCODE -ne 0) { throw "DB JSON file command failed for $SqlPath." }
    $global:LASTEXITCODE = 0
    $json = ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1)
    Assert-True (-not [string]::IsNullOrWhiteSpace($json)) "DB JSON file did not return JSON."
    return ($json | ConvertFrom-Json)
}
function Invoke-NpmCommand {
    param([string[]]$Arguments, [string]$WorkingDirectory)
    $global:LASTEXITCODE = 0
    Push-Location $WorkingDirectory
    try { & npm @Arguments }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "npm command failed: npm $($Arguments -join ' ')" }
    $global:LASTEXITCODE = 0
}
function Assert-NumberCloseOrEqual {
    param([long]$ApiValue, [long]$SqlValue, [long]$Tolerance, [string]$Name)
    $diff = [Math]::Abs($ApiValue - $SqlValue)
    Assert-True ($diff -le $Tolerance) "$Name mismatch. api=$ApiValue sql=$SqlValue tolerance=$Tolerance"
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-07-dashboard-operations-monitoring-check.sql"
$dashboardRoot = Join-Path $repoRoot "src\PosDashboard\SolidPOS.PosDashboard.Admin"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-07-dashboard-operations-monitoring-log.md"

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-07 validation."
Assert-True (Test-Path $sqlPath) "PILOT-07 SQL validator is missing."
Assert-True (Test-Path (Join-Path $dashboardRoot "package.json")) "PosDashboard package.json is missing."
Assert-True ($DatabaseUrl.StartsWith("postgresql://") -or $DatabaseUrl.StartsWith("postgres://")) "DATABASE_URL must be PostgreSQL/Supabase URL."
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
Write-Step "Local repository guardrails PASS"

Write-Step "Local secret scan..."
$global:LASTEXITCODE = 0
& (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
if ($LASTEXITCODE -ne 0) { throw "Local secret scan failed." }
$global:LASTEXITCODE = 0
Write-Step "Local secret scan PASS"

Write-Step "PosDashboard operations monitoring source contract..."
$clientSource = Get-Content -Raw -Path (Join-Path $dashboardRoot "src\api\posServerClient.ts")
$operationsSource = Get-Content -Raw -Path (Join-Path $dashboardRoot "src\features\dashboard\OperationsDashboard.tsx")
Assert-True ($clientSource.Contains("/api/v1/observability/metrics")) "Dashboard client must call /api/v1/observability/metrics."
Assert-True ($clientSource.Contains("getOperationalMetrics")) "Dashboard client must include getOperationalMetrics."
Assert-True ($clientSource.Contains("OperationalMetricsDto")) "Dashboard client must define OperationalMetricsDto."
Assert-True ($operationsSource.Contains("Database monitor")) "Operations dashboard must render Database monitor."
Assert-True ($operationsSource.Contains("API monitor")) "Operations dashboard must render API monitor."
Assert-True ($operationsSource.Contains("Conflict monitor")) "Operations dashboard must render Conflict monitor."
Assert-True ($operationsSource.Contains("Inventory risk")) "Operations dashboard must render Inventory risk."
Write-Step "PosDashboard operations monitoring source contract PASS"

Write-Step "PosDashboard production build and self-test..."
if (-not $SkipNpmInstall) { Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @("install") }
Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @("run", "build")
Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @("run", "self-test")
Write-Step "PosDashboard production build and self-test PASS"

Write-Step "Production liveness/readiness..."
$live = Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq "alive") "Production liveness did not return alive."
$ready = Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq "ready") "Production readiness did not return ready."
Assert-True ($ready.database -eq "ready") "Production database readiness did not return ready."
Write-Step "Production liveness/readiness PASS"

Write-Step "Admin login..."
$loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
Write-Step "Admin login PASS"

Write-Step "Operations monitoring API contract..."
$metrics = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $metrics.generatedAt) "Operational metrics must include generatedAt."
Assert-True ($metrics.database.ready -eq $true) "Operational metrics database.ready must be true."
Assert-True ($metrics.database.requiredTablesPresent -eq $true) "Operational metrics requiredTablesPresent must be true."
Assert-True ($null -ne $metrics.requests.totalRequests) "Operational metrics requests.totalRequests is required."
Assert-True ($null -ne $metrics.requests.p95LatencyMs) "Operational metrics requests.p95LatencyMs is required."
Assert-True ($null -ne $metrics.sync.inboxByStatus) "Operational metrics sync.inboxByStatus is required."
Assert-True ($null -ne $metrics.sync.pendingConflicts) "Operational metrics sync.pendingConflicts is required."
Assert-True ($null -ne $metrics.sync.deadLetterEvents) "Operational metrics sync.deadLetterEvents is required."
Assert-True ($null -ne $metrics.sales.salesLast24Hours) "Operational metrics sales.salesLast24Hours is required."
Assert-True ($null -ne $metrics.payments.failedPaymentsLast24Hours) "Operational metrics payments.failedPaymentsLast24Hours is required."
Assert-True ($null -ne $metrics.inventory.negativeInventoryItemCount) "Operational metrics inventory.negativeInventoryItemCount is required."
Assert-True ($null -ne $metrics.audit.auditEventsLast24Hours) "Operational metrics audit.auditEventsLast24Hours is required."
Write-Step "Operations monitoring API contract PASS"

Write-Step "Dashboard dependency endpoints..."
$syncStatus = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$deadLetter = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$pendingConflicts = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$resolvedConflicts = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=resolved&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
$sales = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sales?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $syncStatus) "Sync status endpoint returned null."
Assert-True ($null -ne $deadLetter) "Dead-letter endpoint returned null."
Assert-True ($null -ne $pendingConflicts) "Pending conflicts endpoint returned null."
Assert-True ($null -ne $resolvedConflicts) "Resolved conflicts endpoint returned null."
Assert-True ($null -ne $auditEvents) "Audit endpoint returned null."
Assert-True ($null -ne $sales) "Sales endpoint returned null."
$deadLetterItems = @(Get-Items -Response $deadLetter)
$pendingConflictItems = @(Get-Items -Response $pendingConflicts)
$resolvedConflictItems = @(Get-Items -Response $resolvedConflicts)
$auditEventItems = @(Get-Items -Response $auditEvents)
$saleItems = @(Get-Items -Response $sales)
Write-Step "Dashboard dependency endpoints PASS"

Write-Step "SQL cross-check against operations metrics..."
$sqlMetrics = Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id = $TenantId }
Assert-True ($sqlMetrics.requiredTablesPresent -eq $true) "SQL required tables are not present."
Assert-True ($sqlMetrics.pilot07SqlValidation -eq "GO") "PILOT-07 SQL validation returned NO-GO."
$apiProcessed = Get-LongValue -Object $metrics.sync.inboxByStatus -Names @("processed")
$apiRetryPending = Get-LongValue -Object $metrics.sync -Names @("retryPendingEvents")
$apiDeadLetter = Get-LongValue -Object $metrics.sync -Names @("deadLetterEvents")
$apiPendingConflicts = Get-LongValue -Object $metrics.sync -Names @("pendingConflicts")
$apiResolvedConflicts = Get-LongValue -Object $metrics.sync -Names @("resolvedConflicts")
$apiSales24 = Get-LongValue -Object $metrics.sales -Names @("salesLast24Hours")
$apiFailedPayments24 = Get-LongValue -Object $metrics.payments -Names @("failedPaymentsLast24Hours")
$apiNegativeInventory = Get-LongValue -Object $metrics.inventory -Names @("negativeInventoryItemCount")
$apiLowStock = Get-LongValue -Object $metrics.inventory -Names @("lowStockItemCount")
Assert-NumberCloseOrEqual -Name "sync.processed" -ApiValue $apiProcessed -SqlValue ([long]$sqlMetrics.processedCount) -Tolerance 0
Assert-NumberCloseOrEqual -Name "sync.retryPending" -ApiValue $apiRetryPending -SqlValue ([long]$sqlMetrics.retryPendingCount) -Tolerance 0
Assert-NumberCloseOrEqual -Name "sync.deadLetter" -ApiValue $apiDeadLetter -SqlValue ([long]$sqlMetrics.deadLetterCount) -Tolerance 0
Assert-NumberCloseOrEqual -Name "sync.pendingConflicts" -ApiValue $apiPendingConflicts -SqlValue ([long]$sqlMetrics.pendingConflictCount) -Tolerance 0
Assert-NumberCloseOrEqual -Name "sync.resolvedConflicts" -ApiValue $apiResolvedConflicts -SqlValue ([long]$sqlMetrics.resolvedConflictCount) -Tolerance 0
Assert-NumberCloseOrEqual -Name "sales.last24h" -ApiValue $apiSales24 -SqlValue ([long]$sqlMetrics.salesLast24Hours) -Tolerance 1
Assert-NumberCloseOrEqual -Name "payments.failed24h" -ApiValue $apiFailedPayments24 -SqlValue ([long]$sqlMetrics.failedPaymentsLast24Hours) -Tolerance 0
Assert-NumberCloseOrEqual -Name "inventory.negative" -ApiValue $apiNegativeInventory -SqlValue ([long]$sqlMetrics.negativeInventoryItemCount) -Tolerance 0
Assert-NumberCloseOrEqual -Name "inventory.lowStock" -ApiValue $apiLowStock -SqlValue ([long]$sqlMetrics.lowStockItemCount) -Tolerance 0
Write-Step "SQL cross-check against operations metrics PASS"

Write-Step "Write pilot log..."
Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS PILOT-07 Dashboard Operations Monitoring Log"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "status: PASS REAL PRODUCTION / GO"
Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"
Add-Content -Path $logPath -Encoding UTF8 -Value "dashboardProject: src/PosDashboard/SolidPOS.PosDashboard.Admin"
Add-Content -Path $logPath -Encoding UTF8 -Value "health: $($metrics.database.ready)"
Add-Content -Path $logPath -Encoding UTF8 -Value "requiredTablesPresent: $($metrics.database.requiredTablesPresent)"
Add-Content -Path $logPath -Encoding UTF8 -Value "activeConnections: $($metrics.database.activeConnections)"
Add-Content -Path $logPath -Encoding UTF8 -Value "totalRequests: $($metrics.requests.totalRequests)"
Add-Content -Path $logPath -Encoding UTF8 -Value "failedRequests: $($metrics.requests.failedRequests)"
Add-Content -Path $logPath -Encoding UTF8 -Value "p95LatencyMs: $($metrics.requests.p95LatencyMs)"
Add-Content -Path $logPath -Encoding UTF8 -Value "processedSync: $apiProcessed"
Add-Content -Path $logPath -Encoding UTF8 -Value "retryPendingSync: $apiRetryPending"
Add-Content -Path $logPath -Encoding UTF8 -Value "deadLetterSync: $apiDeadLetter"
Add-Content -Path $logPath -Encoding UTF8 -Value "pendingConflicts: $apiPendingConflicts"
Add-Content -Path $logPath -Encoding UTF8 -Value "resolvedConflicts: $apiResolvedConflicts"
Add-Content -Path $logPath -Encoding UTF8 -Value "salesLast24Hours: $apiSales24"
Add-Content -Path $logPath -Encoding UTF8 -Value "failedPaymentsLast24Hours: $apiFailedPayments24"
Add-Content -Path $logPath -Encoding UTF8 -Value "negativeInventoryItemCount: $apiNegativeInventory"
Add-Content -Path $logPath -Encoding UTF8 -Value "lowStockItemCount: $apiLowStock"
Add-Content -Path $logPath -Encoding UTF8 -Value "goNoGo: GO"
Write-Step "Write pilot log PASS"

Write-Step "PILOT-07 PASS REAL PRODUCTION / GO"
[pscustomobject]@{
    tenantId = $TenantId
    baseUrl = $script:base
    dashboardProject = "src/PosDashboard/SolidPOS.PosDashboard.Admin"
    generatedAt = $metrics.generatedAt
    databaseReady = $metrics.database.ready
    requiredTablesPresent = $metrics.database.requiredTablesPresent
    activeConnections = $metrics.database.activeConnections
    totalRequests = $metrics.requests.totalRequests
    failedRequests = $metrics.requests.failedRequests
    p95LatencyMs = $metrics.requests.p95LatencyMs
    processedSync = $apiProcessed
    retryPendingSync = $apiRetryPending
    deadLetterSync = $apiDeadLetter
    pendingConflicts = $apiPendingConflicts
    resolvedConflicts = $apiResolvedConflicts
    salesLast24Hours = $apiSales24
    failedPaymentsLast24Hours = $apiFailedPayments24
    negativeInventoryItemCount = $apiNegativeInventory
    lowStockItemCount = $apiLowStock
    auditEventsLast24Hours = $metrics.audit.auditEventsLast24Hours
    schemaVersion = 4
    monitoringContract = "observability_metrics"
    dashboardMonitoring = "ready"
    goNoGo = "GO"
    message = "SolidPOS PILOT-07 dashboard operations monitoring completed."
} | Format-List
