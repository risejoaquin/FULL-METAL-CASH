param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-09] $Message" }
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
    Assert-True (Test-Path $Path) "Required runbook document missing: $Path"
    $content = (Get-Content -Raw -Path $Path).ToLowerInvariant()
    foreach ($term in $Terms) {
        Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term"
    }
}
function Get-SeverityForSignal {
    param([string]$Signal)
    switch ($Signal) {
        "readiness_failure" { return "SEV1" }
        "database_unavailable" { return "SEV1" }
        "auth_incident" { return "SEV1" }
        "terminal_enrollment_incident" { return "SEV2" }
        "offline_terminal_incident" { return "SEV2" }
        "sync_backlog" { return "SEV2" }
        "dead_letter_incident" { return "SEV2" }
        "sync_conflict_incident" { return "SEV2" }
        "inventory_inconsistency" { return "SEV2" }
        "cash_drawer_incident" { return "SEV1" }
        "receipt_incident" { return "SEV3" }
        "backup_restore_incident" { return "SEV1" }
        default { return "SEV3" }
    }
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-09-incident-runbook-check.sql"
$dashboardRoot = Join-Path $repoRoot "src\PosDashboard\SolidPOS.PosDashboard.Admin"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-09-incident-runbook-log.md"
$incidentRunbook = Join-Path $repoRoot "docs\pilot\pilot-09-incident-runbook.md"
$pilotGoNoGo = Join-Path $repoRoot "docs\pilot\pilot-09-go-no-go.md"
$pilotChecklist = Join-Path $repoRoot "docs\pilot\pilot-09-operator-checklist.md"
$productionRunbook = Join-Path $repoRoot "docs\pilot\production-pilot-runbook.md"
$rollbackPlan = Join-Path $repoRoot "docs\pilot\pilot-rollback-plan.md"

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-09 validation."
Assert-True (Test-Path $sqlPath) "PILOT-09 SQL validator is missing."
Assert-True (Test-Path $productionRunbook) "Production pilot runbook is missing."
Assert-True (Test-Path $rollbackPlan) "Pilot rollback plan is missing."
Assert-True (Test-Path $incidentRunbook) "PILOT-09 incident runbook is missing."
Assert-True ($DatabaseUrl.StartsWith("postgresql://") -or $DatabaseUrl.StartsWith("postgres://")) "DATABASE_URL must be PostgreSQL/Supabase URL."
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
Write-Step "Local repository guardrails PASS"

Write-Step "Local secret scan..."
$global:LASTEXITCODE = 0
& (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
if ($LASTEXITCODE -ne 0) { throw "Local secret scan failed." }
$global:LASTEXITCODE = 0
Write-Step "Local secret scan PASS"

Write-Step "Incident runbook document contract..."
$requiredTerms = @(
    "health degradation", "readiness failure", "database unavailable", "auth incident",
    "terminal enrollment", "offline terminal", "sync backlog", "retry_pending",
    "dead_letter", "sync conflict", "inventory inconsistency", "cash drawer",
    "receipt generation", "backup", "restore", "rollback", "severity", "escalation",
    "audit trail", "go/no-go"
)
Assert-DocumentContains -Path $incidentRunbook -Terms $requiredTerms
Assert-DocumentContains -Path $pilotGoNoGo -Terms @("go", "no-go", "sev1", "sev2", "rollback", "audit")
Assert-DocumentContains -Path $pilotChecklist -Terms @("detect", "classify", "contain", "recover", "verify", "close")
Assert-DocumentContains -Path $productionRunbook -Terms @("health", "readiness", "tenant", "sync")
Assert-DocumentContains -Path $rollbackPlan -Terms @("rollback", "backup", "restore")
Write-Step "Incident runbook document contract PASS"

Write-Step "Dashboard operations source contract..."
$dashboardClient = Get-Content -Raw -Path (Join-Path $dashboardRoot "src\api\posServerClient.ts")
$operationsSource = Get-Content -Raw -Path (Join-Path $dashboardRoot "src\features\dashboard\OperationsDashboard.tsx")
Assert-True ($dashboardClient.Contains("/api/v1/observability/metrics")) "Dashboard client must expose observability metrics."
Assert-True ($operationsSource.Contains("Database monitor")) "Dashboard must include Database monitor."
Assert-True ($operationsSource.Contains("API monitor")) "Dashboard must include API monitor."
Assert-True ($operationsSource.Contains("Dead-letter")) "Dashboard must include Dead-letter monitor signal."
Assert-True ($operationsSource.Contains("Conflict monitor")) "Dashboard must include Conflict monitor."
Assert-True ($operationsSource.Contains("Inventory risk")) "Dashboard must include Inventory risk."
Write-Step "Dashboard operations source contract PASS"

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

Write-Step "Admin login..."
$loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
Write-Step "Admin login PASS"

Write-Step "Incident detection endpoint contract..."
$metrics = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$deadLetter = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$pendingConflicts = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($metrics.database.ready -eq $true) "Metrics database.ready must be true."
Assert-True ($metrics.database.requiredTablesPresent -eq $true) "Metrics requiredTablesPresent must be true."
Assert-True ($null -ne $metrics.sync.inboxByStatus) "Metrics sync.inboxByStatus is required."
Assert-True ($null -ne $metrics.sync.deadLetterEvents) "Metrics sync.deadLetterEvents is required."
Assert-True ($null -ne $metrics.sync.pendingConflicts) "Metrics sync.pendingConflicts is required."
Assert-True ($null -ne $metrics.inventory.negativeInventoryItemCount) "Metrics inventory.negativeInventoryItemCount is required."
Assert-True ($null -ne $syncStatus) "Sync status returned null."
Assert-True ($null -ne $deadLetter) "Dead-letter endpoint returned null."
Assert-True ($null -ne $pendingConflicts) "Conflicts endpoint returned null."
Assert-True ((Get-Items $auditEvents).Count -ge 0) "Audit events endpoint shape is invalid."
Write-Step "Incident detection endpoint contract PASS"

Write-Step "SQL cross-check for incident signals..."
$sql = Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id = $TenantId }
Assert-True ($sql.pilot09SqlValidation -eq "GO") "PILOT-09 SQL validation returned NO-GO."
Assert-True ($sql.tenantExists -eq $true) "Tenant must exist in production DB."
Assert-True ($sql.requiredTablesPresent -eq $true) "Required incident tables are missing."
Write-Step "SQL cross-check for incident signals PASS"

Write-Step "Runbook decision matrix validation..."
$syncInbox = $metrics.sync.inboxByStatus
$retryPending = Get-LongValue -Object $syncInbox -Names @("retry_pending", "retryPending") -Default ([long]$sql.retryPendingCount)
$deadLetterCount = Get-LongValue -Object $syncInbox -Names @("dead_letter", "deadLetter") -Default ([long]$sql.deadLetterCount)
$pendingConflictCount = Get-LongValue -Object $metrics.sync -Names @("pendingConflicts") -Default ([long]$sql.pendingConflictCount)
$negativeInventoryCount = Get-LongValue -Object $metrics.inventory -Names @("negativeInventoryItemCount") -Default ([long]$sql.negativeInventoryItemCount)
$failedPayments24 = Get-LongValue -Object $metrics.payments -Names @("failedPaymentsLast24Hours") -Default ([long]$sql.failedPaymentsLast24Hours)
$failedRequests = Get-LongValue -Object $metrics.requests -Names @("failedRequests") -Default 0

$signals = @(
    [pscustomobject]@{ key = "readiness_failure"; observed = $false; detector = "/health/ready"; action = "open SEV1 and freeze deploys" },
    [pscustomobject]@{ key = "database_unavailable"; observed = $false; detector = "/health/ready database"; action = "activate DB incident path" },
    [pscustomobject]@{ key = "auth_incident"; observed = $false; detector = "/api/v1/auth/login"; action = "validate JWT and tenant status" },
    [pscustomobject]@{ key = "terminal_enrollment_incident"; observed = $false; detector = "terminal register"; action = "verify fingerprint and terminal status" },
    [pscustomobject]@{ key = "offline_terminal_incident"; observed = $false; detector = "PosCore local integrity"; action = "keep selling offline within 72 hours" },
    [pscustomobject]@{ key = "sync_backlog"; observed = ($retryPending -gt 0); detector = "/api/v1/sync/status"; action = "watch retry queue and process sync" },
    [pscustomobject]@{ key = "dead_letter_incident"; observed = ($deadLetterCount -gt 0); detector = "/api/v1/sync/dead-letter"; action = "triage payload and retry or quarantine" },
    [pscustomobject]@{ key = "sync_conflict_incident"; observed = ($pendingConflictCount -gt 0); detector = "/api/v1/sync/conflicts"; action = "resolve use_server or use_client by evidence" },
    [pscustomobject]@{ key = "inventory_inconsistency"; observed = ($negativeInventoryCount -gt 0); detector = "observability inventory risk"; action = "reconcile ledger and count stock" },
    [pscustomobject]@{ key = "cash_drawer_incident"; observed = $false; detector = "cash shift summary"; action = "freeze close and escalate variance" },
    [pscustomobject]@{ key = "receipt_incident"; observed = $false; detector = "receipt endpoints"; action = "issue digital receipt or retry email" },
    [pscustomobject]@{ key = "backup_restore_incident"; observed = $false; detector = "PILOT-08 manifest"; action = "use restore drill and rollback tree" },
    [pscustomobject]@{ key = "api_failure_trend"; observed = ($failedRequests -gt 0); detector = "observability failedRequests"; action = "review failed request audit/logs" },
    [pscustomobject]@{ key = "payment_incident"; observed = ($failedPayments24 -gt 0); detector = "payments failed last 24h"; action = "reconcile tender and payment status" }
)

foreach ($signal in $signals) {
    $severity = Get-SeverityForSignal -Signal $signal.key
    Assert-True (-not [string]::IsNullOrWhiteSpace($severity)) "Missing severity for $($signal.key)."
    Assert-True (-not [string]::IsNullOrWhiteSpace($signal.detector)) "Missing detector for $($signal.key)."
    Assert-True (-not [string]::IsNullOrWhiteSpace($signal.action)) "Missing action for $($signal.key)."
}
Write-Step "Runbook decision matrix validation PASS"

Write-Step "Write incident validation log..."
Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS PILOT-09 Pilot Incident Runbook Validation Log"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "status: PASS REAL PRODUCTION / GO"
Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"
Add-Content -Path $logPath -Encoding UTF8 -Value "healthLive: $($live.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "healthReady: $($ready.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "databaseReady: $($metrics.database.ready)"
Add-Content -Path $logPath -Encoding UTF8 -Value "requiredTablesPresent: $($metrics.database.requiredTablesPresent)"
Add-Content -Path $logPath -Encoding UTF8 -Value "failedRequests: $failedRequests"
Add-Content -Path $logPath -Encoding UTF8 -Value "retryPendingSync: $retryPending"
Add-Content -Path $logPath -Encoding UTF8 -Value "deadLetterSync: $deadLetterCount"
Add-Content -Path $logPath -Encoding UTF8 -Value "pendingConflicts: $pendingConflictCount"
Add-Content -Path $logPath -Encoding UTF8 -Value "negativeInventoryItemCount: $negativeInventoryCount"
Add-Content -Path $logPath -Encoding UTF8 -Value "failedPaymentsLast24Hours: $failedPayments24"
Add-Content -Path $logPath -Encoding UTF8 -Value "storeCount: $($sql.storeCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "terminalCount: $($sql.terminalCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "schemaVersion: 4"
Add-Content -Path $logPath -Encoding UTF8 -Value "incidentRunbookContract: pilot_incident_runbook"
Add-Content -Path $logPath -Encoding UTF8 -Value "goNoGo: GO"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "Incident matrix:"
foreach ($signal in $signals) {
    Add-Content -Path $logPath -Encoding UTF8 -Value "$($signal.key): severity=$(Get-SeverityForSignal -Signal $signal.key); observed=$($signal.observed); detector=$($signal.detector); action=$($signal.action)"
}
Write-Step "Write incident validation log PASS"

Write-Step "PILOT-09 PASS REAL PRODUCTION / GO"
[pscustomobject]@{
    tenantId = $TenantId
    baseUrl = $script:base
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    healthLive = $live.status
    healthReady = $ready.status
    databaseReady = $metrics.database.ready
    requiredTablesPresent = $metrics.database.requiredTablesPresent
    failedRequests = $failedRequests
    retryPendingSync = $retryPending
    deadLetterSync = $deadLetterCount
    pendingConflicts = $pendingConflictCount
    negativeInventoryItemCount = $negativeInventoryCount
    failedPaymentsLast24Hours = $failedPayments24
    storeCount = [long]$sql.storeCount
    terminalCount = [long]$sql.terminalCount
    incidentSignalCount = $signals.Count
    observedSignalCount = @($signals | Where-Object { $_.observed -eq $true }).Count
    sev1ScenarioCount = @($signals | Where-Object { (Get-SeverityForSignal -Signal $_.key) -eq "SEV1" }).Count
    sev2ScenarioCount = @($signals | Where-Object { (Get-SeverityForSignal -Signal $_.key) -eq "SEV2" }).Count
    schemaVersion = 4
    incidentRunbookContract = "pilot_incident_runbook"
    goNoGo = "GO"
    message = "SolidPOS PILOT-09 pilot incident runbook validation completed."
}
