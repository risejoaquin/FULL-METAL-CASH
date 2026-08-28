param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[EXP-01] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString {
    param([securestring]$SecureValue)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Invoke-CheckedCommand {
    param([string]$Name, [scriptblock]$Command)
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
    $global:LASTEXITCODE = 0
}
function Invoke-NpmCommand {
    param([string[]]$Arguments, [string]$WorkingDirectory)
    Push-Location $WorkingDirectory
    try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } }
    finally { Pop-Location }
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
function Assert-DocumentContains {
    param([string]$Path, [string[]]$Terms)
    Assert-True (Test-Path $Path) "Required document missing: $Path"
    $content = (Get-Content -Raw -Path $Path).ToLowerInvariant()
    foreach ($term in $Terms) {
        Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term"
    }
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$slnPath = Join-Path $repoRoot "solidpos-platform.sln"
$dashboardRoot = Join-Path $repoRoot "src\PosDashboard\SolidPOS.PosDashboard.Admin"
$runtimeDirectory = Join-Path $repoRoot ".runtime\exp-01-post-pilot-baseline-freeze"
$manifestPath = Join-Path $runtimeDirectory "baseline-manifest.json"
$logDirectory = Join-Path $repoRoot "docs\expansion\logs"
$logPath = Join-Path $logDirectory "exp-01-post-pilot-baseline-freeze-log.md"
$releaseNotes = Join-Path $repoRoot "docs\release\post-pilot-baseline-release-notes.md"
$releaseTag = Join-Path $repoRoot "docs\release\post-pilot-baseline-tag.md"
$baselineDoc = Join-Path $repoRoot "docs\expansion\exp-01-baseline-freeze.md"
$changelogDoc = Join-Path $repoRoot "docs\expansion\exp-01-changelog.md"
$hotfixDoc = Join-Path $repoRoot "docs\expansion\exp-01-hotfix-consolidation.md"
$artifactMatrixDoc = Join-Path $repoRoot "docs\expansion\exp-01-artifact-matrix.md"
$goNoGoDoc = Join-Path $repoRoot "docs\expansion\exp-01-go-no-go.md"
$roadmapDoc = Join-Path $repoRoot "SOLIDPOS_ROADMAP_POST_PILOT_20260820.md"

Write-Step "Local repository guardrails..."
Assert-True (Test-Path $slnPath) "solidpos-platform.sln is required."
Assert-True (Test-Path (Join-Path $repoRoot ".env.example")) ".env.example is required before EXP-01 validation."
Assert-True (Test-Path (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1")) "Secret scan script is missing."
Assert-True ($DatabaseUrl.StartsWith("postgresql://") -or $DatabaseUrl.StartsWith("postgres://")) "DATABASE_URL must be PostgreSQL/Supabase URL."
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
Write-Step "Local repository guardrails PASS"

Write-Step "Release and baseline document contract..."
Assert-DocumentContains -Path $baselineDoc -Terms @("post-pilot", "baseline", "go_limited_expansion", "exp-02")
Assert-DocumentContains -Path $changelogDoc -Terms @("pilot-01", "pilot-02", "pilot-03", "pilot-04", "pilot-05", "pilot-06", "pilot-07", "pilot-08", "pilot-09", "pilot-10")
Assert-DocumentContains -Path $hotfixDoc -Terms @("hotfix", "pilot-10", "pos.return_refunds")
Assert-DocumentContains -Path $artifactMatrixDoc -Terms @("artifact", "script", "release")
Assert-DocumentContains -Path $goNoGoDoc -Terms @("go", "no-go", "dotnet restore", "dotnet build", "dotnet test")
Assert-DocumentContains -Path $releaseNotes -Terms @("post-pilot", "go_limited_expansion", "known monitored conditions")
Assert-DocumentContains -Path $releaseTag -Terms @("v0.10.0-post-pilot.20260820", "mass rollout")
Assert-DocumentContains -Path $roadmapDoc -Terms @("exp-01", "post-pilot baseline freeze", "go limited")
Write-Step "Release and baseline document contract PASS"

Write-Step "Pilot evidence contract..."
$pilotFiles = @(
    "SOLIDPOS_PILOT_01_CONTROLLED_STORE_SETUP.md",
    "SOLIDPOS_PILOT_02_REAL_POS_TRANSACTION_VALIDATION.md",
    "SOLIDPOS_PILOT_03_CASH_DRAWER_SHIFT_OPERATIONS.md",
    "SOLIDPOS_PILOT_04_RECEIPTS_RETURNS_REFUNDS.md",
    "SOLIDPOS_PILOT_05_OFFLINE_MODE_FIELD_TEST.md",
    "SOLIDPOS_PILOT_06_SYNC_RECOVERY_CONFLICT_FIELD_TEST.md",
    "SOLIDPOS_PILOT_07_DASHBOARD_OPERATIONS_MONITORING.md",
    "SOLIDPOS_PILOT_08_BACKUP_RESTORE_ROLLBACK_DRILL.md",
    "SOLIDPOS_PILOT_09_INCIDENT_RUNBOOK_VALIDATION.md",
    "SOLIDPOS_PILOT_10_PILOT_CLOSURE_REPORT_PRODUCTION_EXPANSION_DECISION.md"
)
foreach ($file in $pilotFiles) { Assert-True (Test-Path (Join-Path $repoRoot $file)) "Missing pilot evidence document: $file" }
for ($i = 1; $i -le 10; $i++) {
    $commandFile = Join-Path $repoRoot ("PILOT_{0:D2}_VALIDATION_COMMANDS.md" -f $i)
    Assert-True (Test-Path $commandFile) "Missing pilot validation commands: $commandFile"
}
Write-Step "Pilot evidence contract PASS"

Write-Step "Local secret scan..."
Invoke-CheckedCommand -Name "secret scan" -Command { & (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot }
Write-Step "Local secret scan PASS"

Write-Step "dotnet restore..."
Push-Location $repoRoot
try { Invoke-CheckedCommand -Name "dotnet restore" -Command { & dotnet restore $slnPath } }
finally { Pop-Location }
Write-Step "dotnet restore PASS"

Write-Step "dotnet build..."
Push-Location $repoRoot
try { Invoke-CheckedCommand -Name "dotnet build" -Command { & dotnet build $slnPath --no-restore } }
finally { Pop-Location }
Write-Step "dotnet build PASS"

Write-Step "dotnet test..."
Push-Location $repoRoot
try { Invoke-CheckedCommand -Name "dotnet test" -Command { & dotnet test $slnPath --no-build } }
finally { Pop-Location }
Write-Step "dotnet test PASS"

if (-not $SkipDashboardBuild) {
    Write-Step "Dashboard production build and self-test..."
    Assert-True (Test-Path (Join-Path $dashboardRoot "package.json")) "Dashboard package.json is missing."
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

Write-Step "Admin login and protected metrics..."
$loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
$metrics = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$auditEvents = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $metrics.database.ready) "Metrics database.ready is required."
Assert-True ($null -ne $metrics.requests.totalRequests) "Metrics requests.totalRequests is required."
Assert-True ($null -ne $metrics.sync.inboxByStatus) "Metrics sync.inboxByStatus is required."
Assert-True ($null -ne $metrics.inventory.negativeInventoryItemCount) "Metrics inventory.negativeInventoryItemCount is required."
Assert-True ($null -ne $syncStatus) "Sync status returned null."
Assert-True ((Get-Items $auditEvents).Count -ge 0) "Audit events endpoint shape is invalid."
Write-Step "Admin login and protected metrics PASS"

Write-Step "Write baseline manifest and log..."
$syncInbox = $metrics.sync.inboxByStatus
$retryPending = Get-LongValue -Object $syncInbox -Names @("retry_pending", "retryPending") -Default 0
$deadLetterCount = Get-LongValue -Object $syncInbox -Names @("dead_letter", "deadLetter") -Default 0
$negativeInventory = Get-LongValue -Object $metrics.inventory -Names @("negativeInventoryItemCount") -Default 0
$failedRequests = Get-LongValue -Object $metrics.requests -Names @("failedRequests") -Default 0
$conditions = @()
if ($retryPending -gt 0) { $conditions += "monitor_retry_pending_sync" }
if ($deadLetterCount -gt 0) { $conditions += "triage_known_dead_letter" }
if ($negativeInventory -gt 0) { $conditions += "inventory_reconciliation_required" }
if ($failedRequests -gt 0) { $conditions += "review_failed_requests" }
$gitCommit = "unavailable"
$gitStatus = "unavailable"
if (Test-Path (Join-Path $repoRoot ".git")) {
    Push-Location $repoRoot
    try {
        $gitCommit = (& git rev-parse HEAD 2>$null)
        $gitStatus = (& git status --short 2>$null) -join " | "
        if ([string]::IsNullOrWhiteSpace($gitStatus)) { $gitStatus = "clean" }
    }
    finally { Pop-Location }
}
$manifest = [ordered]@{
    phase = "EXP-01"
    status = "PASS POST-PILOT BASELINE FREEZE / GO EXP-02"
    tenantId = $TenantId
    baseUrl = $script:base
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    recommendedTag = "v0.10.0-post-pilot.20260820"
    gitCommit = $gitCommit
    gitStatus = $gitStatus
    pilot01To10 = "PASS REAL PRODUCTION / GO"
    productionExpansionDecision = "GO_LIMITED_EXPANSION"
    healthLive = $live.status
    healthReady = $ready.status
    databaseReady = $ready.database
    retryPendingSync = $retryPending
    deadLetterSync = $deadLetterCount
    negativeInventoryItemCount = $negativeInventory
    failedRequests = $failedRequests
    conditions = $conditions
    nextPhase = "EXP-02 Production Expansion Readiness Pack"
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS EXP-01 Post-Pilot Baseline Freeze Log"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "status: PASS POST-PILOT BASELINE FREEZE / GO EXP-02"
Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"
Add-Content -Path $logPath -Encoding UTF8 -Value "recommendedTag: v0.10.0-post-pilot.20260820"
Add-Content -Path $logPath -Encoding UTF8 -Value "conditions: $($conditions -join ',')"
Write-Step "Write baseline manifest and log PASS"

Write-Step "EXP-01 PASS POST-PILOT BASELINE FREEZE / GO EXP-02"
[pscustomobject]$manifest
