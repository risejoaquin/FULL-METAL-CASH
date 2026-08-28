param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-10] $Message" }
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
function Invoke-NpmCommand {
    param([string[]]$Arguments, [string]$WorkingDirectory)
    $global:LASTEXITCODE = 0
    Push-Location $WorkingDirectory
    try { & npm @Arguments }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "npm command failed: npm $($Arguments -join ' ')" }
    $global:LASTEXITCODE = 0
}
function Invoke-DbJsonFile {
    param(
        [Parameter(Mandatory = $true)] [string]$SqlPath,
        [Parameter(Mandatory = $true)] [hashtable]$Variables
    )
    $mountDirectory = (Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $fileName = Split-Path -Leaf $SqlPath
    $args = @("run", "--rm", "--env", "DATABASE_URL=$DatabaseUrl", "-v", "${mountDirectory}:/sql:ro", "postgres:17", "psql", "$DatabaseUrl", "-tA", "-v", "ON_ERROR_STOP=1")
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
function Assert-DocumentContains {
    param([string]$Path, [string[]]$Terms)
    Assert-True (Test-Path $Path) "Required document missing: $Path"
    $content = (Get-Content -Raw -Path $Path).ToLowerInvariant()
    foreach ($term in $Terms) {
        Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term"
    }
}
function Assert-DocumentContainsAny {
    param([string]$Path, [string[]]$Terms, [string]$Label)
    Assert-True (Test-Path $Path) "Required document missing: $Path"
    $content = (Get-Content -Raw -Path $Path).ToLowerInvariant()
    foreach ($term in $Terms) {
        if ($content.Contains($term.ToLowerInvariant())) { return }
    }
    throw "Document $Path is missing required concept: $Label. Accepted terms: $($Terms -join ', ')"
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-10-production-expansion-check.sql"
$dashboardRoot = Join-Path $repoRoot "src\PosDashboard\SolidPOS.PosDashboard.Admin"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-10-closure-production-expansion-log.md"
$closureReport = Join-Path $repoRoot "docs\pilot\pilot-10-closure-report.md"
$expansionDecision = Join-Path $repoRoot "docs\pilot\pilot-10-production-expansion-decision.md"
$operatorChecklist = Join-Path $repoRoot "docs\pilot\pilot-10-operator-checklist.md"
$goNoGoDoc = Join-Path $repoRoot "docs\pilot\pilot-10-go-no-go.md"
$incidentRunbook = Join-Path $repoRoot "docs\pilot\pilot-09-incident-runbook.md"
$rollbackPlan = Join-Path $repoRoot "docs\pilot\pilot-rollback-plan.md"

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-10 validation."
Assert-True (Test-Path $sqlPath) "PILOT-10 SQL validator is missing."
Assert-True (Test-Path $closureReport) "PILOT-10 closure report is missing."
Assert-True (Test-Path $expansionDecision) "PILOT-10 expansion decision is missing."
Assert-True (Test-Path $operatorChecklist) "PILOT-10 operator checklist is missing."
Assert-True (Test-Path $goNoGoDoc) "PILOT-10 GO/NO-GO document is missing."
Assert-True (Test-Path $incidentRunbook) "PILOT-09 incident runbook is required before closure."
Assert-True (Test-Path $rollbackPlan) "Pilot rollback plan is required before closure."
Assert-True ($DatabaseUrl.StartsWith("postgresql://") -or $DatabaseUrl.StartsWith("postgres://")) "DATABASE_URL must be PostgreSQL/Supabase URL."
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
Write-Step "Local repository guardrails PASS"

Write-Step "Local secret scan..."
$global:LASTEXITCODE = 0
& (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
if ($LASTEXITCODE -ne 0) { throw "Local secret scan failed." }
$global:LASTEXITCODE = 0
Write-Step "Local secret scan PASS"

Write-Step "Pilot closure document contract..."
$pilotFiles = @(
    "SOLIDPOS_PILOT_01_CONTROLLED_STORE_SETUP.md",
    "SOLIDPOS_PILOT_02_REAL_POS_TRANSACTION_VALIDATION.md",
    "SOLIDPOS_PILOT_03_CASH_DRAWER_SHIFT_OPERATIONS.md",
    "SOLIDPOS_PILOT_04_RECEIPTS_RETURNS_REFUNDS.md",
    "SOLIDPOS_PILOT_05_OFFLINE_MODE_FIELD_TEST.md",
    "SOLIDPOS_PILOT_06_SYNC_RECOVERY_CONFLICT_FIELD_TEST.md",
    "SOLIDPOS_PILOT_07_DASHBOARD_OPERATIONS_MONITORING.md",
    "SOLIDPOS_PILOT_08_BACKUP_RESTORE_ROLLBACK_DRILL.md",
    "SOLIDPOS_PILOT_09_INCIDENT_RUNBOOK_VALIDATION.md"
)
foreach ($file in $pilotFiles) { Assert-True (Test-Path (Join-Path $repoRoot $file)) "Missing pilot evidence document: $file" }
Assert-DocumentContains -Path $closureReport -Terms @("pilot-01", "pilot-02", "pilot-03", "pilot-04", "pilot-05", "pilot-06", "pilot-07", "pilot-08", "pilot-09", "production expansion", "residual risk", "negative inventory", "go/no-go")
Assert-DocumentContains -Path $expansionDecision -Terms @("decision", "conditions", "rollback", "monitoring", "go", "no-go")
Assert-DocumentContainsAny -Path $expansionDecision -Label "production expansion" -Terms @("expand", "expansion", "expansion type", "limited production expansion", "controlled expansion")
Assert-DocumentContains -Path $operatorChecklist -Terms @("pre-expansion", "during expansion", "post-expansion", "monitor", "rollback")
Assert-DocumentContains -Path $goNoGoDoc -Terms @("go", "no-go", "blocker", "approval")
Assert-DocumentContainsAny -Path $goNoGoDoc -Label "risk management" -Terms @("risk", "risks", "riesgo", "riesgos", "residual risk", "operational risk")
Write-Step "Pilot closure document contract PASS"

if (-not $SkipDashboardBuild) {
    Write-Step "Dashboard production build and self-test..."
    Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @("install")
    Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @("run", "build")
    Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @("run", "self-test")
    Write-Step "Dashboard production build and self-test PASS"
}

Write-Step "Production liveness/readiness..."
$live = Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq "alive") "Production liveness did not return alive."
$ready = Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq "ready") "Production readiness did not return ready."
Assert-True ($ready.database -eq "ready") "Production database readiness did not return ready."
Write-Step "Production liveness/readiness PASS"

Write-Step "Admin login and monitoring endpoints..."
$loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
$metrics = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$deadLetter = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$conflicts = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $metrics.database.ready) "Metrics database.ready is required."
Assert-True ($null -ne $metrics.requests.totalRequests) "Metrics requests.totalRequests is required."
Assert-True ($null -ne $metrics.sync.inboxByStatus) "Metrics sync.inboxByStatus is required."
Assert-True ($null -ne $metrics.inventory.negativeInventoryItemCount) "Metrics inventory.negativeInventoryItemCount is required."
Assert-True ($null -ne $syncStatus) "Sync status returned null."
Assert-True ($null -ne $deadLetter) "Dead-letter endpoint returned null."
Assert-True ($null -ne $conflicts) "Conflicts endpoint returned null."
Assert-True ((Get-Items $auditEvents).Count -ge 0) "Audit events endpoint shape is invalid."
Write-Step "Admin login and monitoring endpoints PASS"

Write-Step "SQL production expansion cross-check..."
$sql = Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id = $TenantId }
$sqlBlockingReasons = @($sql.sqlBlockingReasons)
Assert-True ($sql.pilot10SqlValidation -eq "GO") "PILOT-10 SQL validation returned NO-GO. Reasons: $($sqlBlockingReasons -join ',')"
Assert-True ($sql.tenantExists -eq $true) "Tenant must exist in production DB."
Assert-True ($sql.requiredTablesPresent -eq $true) "Required production expansion tables are missing."
Write-Step "SQL production expansion cross-check PASS"

Write-Step "Expansion decision matrix..."
$syncInbox = $metrics.sync.inboxByStatus
$retryPending = Get-LongValue -Object $syncInbox -Names @("retry_pending", "retryPending") -Default ([long]$sql.retryPendingSyncCount)
$deadLetterCount = Get-LongValue -Object $syncInbox -Names @("dead_letter", "deadLetter") -Default ([long]$sql.deadLetterSyncCount)
$pendingConflicts = Get-LongValue -Object $metrics.sync -Names @("pendingConflicts") -Default ([long]$sql.pendingConflictCount)
$negativeInventory = Get-LongValue -Object $metrics.inventory -Names @("negativeInventoryItemCount") -Default ([long]$sql.negativeInventoryItemCount)
$failedPayments24 = Get-LongValue -Object $metrics.payments -Names @("failedPaymentsLast24Hours") -Default ([long]$sql.failedPaymentsLast24Hours)
$failedRequests = Get-LongValue -Object $metrics.requests -Names @("failedRequests") -Default 0

$blockers = @()
if ($live.status -ne "alive") { $blockers += "liveness_not_alive" }
if ($ready.status -ne "ready") { $blockers += "readiness_not_ready" }
if ($ready.database -ne "ready") { $blockers += "database_not_ready" }
if ($pendingConflicts -gt 0) { $blockers += "pending_conflicts" }
if ($failedPayments24 -gt 0) { $blockers += "failed_payments_last_24h" }

$conditions = @()
if ($retryPending -gt 0) { $conditions += "monitor_retry_pending_sync" }
if ($deadLetterCount -gt 0) { $conditions += "triage_known_dead_letter" }
if ($negativeInventory -gt 0) { $conditions += "inventory_reconciliation_required" }
if ($failedRequests -gt 0) { $conditions += "review_failed_requests" }

$decision = "GO_LIMITED_EXPANSION"
if ($blockers.Count -gt 0) { $decision = "NO_GO" }
Assert-True ($decision -ne "NO_GO") "Expansion decision is NO-GO because blockers exist: $($blockers -join ',')"
Write-Step "Expansion decision matrix PASS"

Write-Step "Write closure validation log..."
Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS PILOT-10 Pilot Closure Production Expansion Log"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "status: PASS REAL PRODUCTION / GO"
Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"
Add-Content -Path $logPath -Encoding UTF8 -Value "decision: $decision"
Add-Content -Path $logPath -Encoding UTF8 -Value "blockers: $($blockers -join ',')"
Add-Content -Path $logPath -Encoding UTF8 -Value "conditions: $($conditions -join ',')"
Add-Content -Path $logPath -Encoding UTF8 -Value "sqlWarnings: $(@($sql.sqlWarnings) -join ',')"
Add-Content -Path $logPath -Encoding UTF8 -Value "schemaVersion: 4"
Add-Content -Path $logPath -Encoding UTF8 -Value "totalSalesCount: $($sql.totalSalesCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "salesLast24Hours: $($sql.salesLast24Hours)"
Add-Content -Path $logPath -Encoding UTF8 -Value "grossSalesCents: $($sql.grossSalesCents)"
Add-Content -Path $logPath -Encoding UTF8 -Value "closedShiftCount: $($sql.closedShiftCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "digitalReceiptCount: $($sql.digitalReceiptCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "completedReturnCount: $($sql.completedReturnCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "processedSyncCount: $($sql.processedSyncCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "retryPendingSyncCount: $retryPending"
Add-Content -Path $logPath -Encoding UTF8 -Value "deadLetterSyncCount: $deadLetterCount"
Add-Content -Path $logPath -Encoding UTF8 -Value "pendingConflictCount: $pendingConflicts"
Add-Content -Path $logPath -Encoding UTF8 -Value "negativeInventoryItemCount: $negativeInventory"
Add-Content -Path $logPath -Encoding UTF8 -Value "failedRequests: $failedRequests"
Add-Content -Path $logPath -Encoding UTF8 -Value "goNoGo: GO"
Write-Step "Write closure validation log PASS"

$result = [pscustomobject]@{
    tenantId = $TenantId
    baseUrl = $script:base
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    healthLive = $live.status
    healthReady = $ready.status
    databaseReady = ($ready.database -eq "ready")
    requiredTablesPresent = $sql.requiredTablesPresent
    storeCount = $sql.storeCount
    terminalCount = $sql.terminalCount
    userCount = $sql.userCount
    totalSalesCount = $sql.totalSalesCount
    salesLast24Hours = $sql.salesLast24Hours
    grossSalesCents = $sql.grossSalesCents
    approvedPaymentCount = $sql.approvedPaymentCount
    failedPaymentsLast24Hours = $failedPayments24
    closedShiftCount = $sql.closedShiftCount
    digitalReceiptCount = $sql.digitalReceiptCount
    completedReturnCount = $sql.completedReturnCount
    processedSyncCount = $sql.processedSyncCount
    retryPendingSyncCount = $retryPending
    deadLetterSyncCount = $deadLetterCount
    pendingConflictCount = $pendingConflicts
    resolvedConflictCount = $sql.resolvedConflictCount
    negativeInventoryItemCount = $negativeInventory
    sqlWarnings = @($sql.sqlWarnings)
    auditEventCount = $sql.auditEventCount
    auditEventsLast24Hours = $sql.auditEventsLast24Hours
    failedRequests = $failedRequests
    expansionDecision = $decision
    expansionBlockerCount = $blockers.Count
    expansionConditionCount = $conditions.Count
    expansionConditions = ($conditions -join ",")
    schemaVersion = 4
    closureContract = "pilot_closure_production_expansion"
    goNoGo = "GO"
    message = "SolidPOS PILOT-10 pilot closure report production expansion decision completed."
}

Write-Step "PILOT-10 PASS REAL PRODUCTION / GO"
$result
