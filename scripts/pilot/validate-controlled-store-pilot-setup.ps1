param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$Email,

    [securestring]$Password,

    [string]$DatabaseUrl = $env:DATABASE_URL,

    [string]$StoreCode = 'MAIN',

    [string]$ProductSku = 'QSR-AMERICANO',

    [string]$PaymentMethodCode = 'cash',

    [string]$PilotLogPath = 'docs/pilot/logs/pilot-01-daily-log.md',

    [switch]$SkipDashboardBuild,

    [switch]$SkipLocalSecretScan
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory = $true)][securestring]$Value)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Invoke-PilotSetupStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "[PILOT-01] $Name..."
    & $Action
    Write-Host "[PILOT-01] $Name PASS"
}

if ($null -eq $Password) {
    $Password = Read-Host -AsSecureString "Admin password for $Email"
}

$plainPassword = Convert-SecureStringToPlainText -Value $Password
if ([string]::IsNullOrWhiteSpace($plainPassword)) { throw 'Password cannot be empty.' }
if ([string]::IsNullOrWhiteSpace($DatabaseUrl)) { throw 'DATABASE_URL is required. Set $env:DATABASE_URL or pass -DatabaseUrl.' }

$rootDir = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sqlFile = Join-Path $rootDir 'scripts/pilot/pilot-01-store-setup-check.sql'

Invoke-PilotSetupStep -Name 'Local repository guardrails' -Action {
    $requiredPaths = @(
        '.gitignore',
        'scripts/security/scan-local-secrets.ps1',
        'scripts/posdashboard/validate-posdashboard-operations-dashboard.ps1',
        'scripts/pilot/validate-production-pilot-readiness.ps1',
        'scripts/pilot/validate-controlled-store-pilot-setup.ps1',
        'scripts/pilot/pilot-01-store-setup-check.sql',
        'docs/pilot/controlled-store-pilot-setup.md',
        'docs/pilot/daily-opening-checklist.md',
        'docs/pilot/daily-closing-checklist.md',
        'docs/pilot/pilot-rollback-plan.md'
    )

    foreach ($path in $requiredPaths) {
        if (-not (Test-Path $path)) { throw "Required PILOT-01 artifact is missing: $path" }
    }

    $gitignore = Get-Content '.gitignore' -Raw
    $requiredRules = @('.env', '.runtime/', 'node_modules/', 'dist/', '*.sqlite', '*.pkg', '*.log')
    foreach ($rule in $requiredRules) {
        if ($gitignore -notlike "*$rule*") { throw ".gitignore is missing required rule: $rule" }
    }
}

if (-not $SkipLocalSecretScan) {
    Invoke-PilotSetupStep -Name 'Local secret scan' -Action {
        & .\scripts\security\scan-local-secrets.ps1
    }
}

if (-not $SkipDashboardBuild) {
    Invoke-PilotSetupStep -Name 'PosDashboard production build and self-test' -Action {
        & .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1 -BaseUrl $BaseUrl -TenantId $TenantId -Email $Email
    }
}

Invoke-PilotSetupStep -Name 'Production liveness' -Action {
    $script:live = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/live" -TimeoutSec 30
    if ($script:live.status -ne 'alive') { throw "Liveness failed: $($script:live | ConvertTo-Json -Depth 10)" }
}

Invoke-PilotSetupStep -Name 'Production readiness' -Action {
    $script:ready = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/ready" -TimeoutSec 30
    if ($script:ready.status -ne 'ready' -or $script:ready.database -ne 'ready') { throw "Readiness failed: $($script:ready | ConvertTo-Json -Depth 10)" }
}

Invoke-PilotSetupStep -Name 'Admin login' -Action {
    $body = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
    $script:session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/auth/login" -ContentType 'application/json' -Body $body -TimeoutSec 30
    if ([string]::IsNullOrWhiteSpace($script:session.accessToken)) { throw 'Login did not return accessToken.' }
}

$headers = @{ Authorization = "Bearer $($script:session.accessToken)" }

Invoke-PilotSetupStep -Name 'Protected metrics' -Action {
    $script:metrics = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/observability/metrics" -Headers $headers -TimeoutSec 30
    if (-not $script:metrics.database.ready) { throw 'Metrics database.ready was false.' }
    if (-not $script:metrics.database.requiredTablesPresent) { throw 'Metrics database.requiredTablesPresent was false.' }
}

Invoke-PilotSetupStep -Name 'Sync runtime status' -Action {
    $script:syncStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sync/status" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:syncStatus) { throw 'Sync status returned null.' }
}

Invoke-PilotSetupStep -Name 'Sales read model availability' -Action {
    $script:sales = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sales?limit=10" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:sales) { throw 'Sales read model returned null.' }
}

Invoke-PilotSetupStep -Name 'Audit events read model availability' -Action {
    $script:auditEvents = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/audit/events?limit=10" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:auditEvents) { throw 'Audit events read model returned null.' }
}

Invoke-PilotSetupStep -Name 'Controlled store data setup via PostgreSQL' -Action {
    if (-not (Test-Path $sqlFile)) { throw "SQL file not found: $sqlFile" }

    docker run --rm `
      --env "DATABASE_URL=$DatabaseUrl" `
      -v "${rootDir}:/work" `
      -w /work `
      postgres:16 `
      psql "$DatabaseUrl" `
        -v "tenant_id=$TenantId" `
        -v "store_code=$StoreCode" `
        -v "admin_email=$Email" `
        -v "product_sku=$ProductSku" `
        -v "payment_method_code=$PaymentMethodCode" `
        -f scripts/pilot/pilot-01-store-setup-check.sql

    if ($LASTEXITCODE -ne 0) { throw 'Controlled store setup SQL validation failed.' }
}

Invoke-PilotSetupStep -Name 'Pilot daily log initialized' -Action {
    $pilotLogFullPath = Join-Path $rootDir $PilotLogPath
    $pilotLogDirectory = Split-Path $pilotLogFullPath -Parent
    if (-not (Test-Path $pilotLogDirectory)) { New-Item -ItemType Directory -Force -Path $pilotLogDirectory | Out-Null }

    if (-not (Test-Path $pilotLogFullPath)) {
        @"
# SolidPOS Pilot Daily Log

## Pilot Day 01

- Date:
- Operator:
- Opening GO/NO-GO:
- First sale ID:
- Receipt generated:
- Cash shift status:
- Sync status:
- Dashboard status:
- Incidents:
- Closing GO/NO-GO:
"@ | Set-Content -Path $pilotLogFullPath -Encoding UTF8
    }
}

$saleCount = @($script:sales).Count
$auditCount = if ($null -ne $script:auditEvents.items) { @($script:auditEvents.items).Count } else { @($script:auditEvents).Count }

$plainPassword = $null
[GC]::Collect()

Write-Host ''
[pscustomobject]@{
    tenantId = $TenantId
    baseUrl = $BaseUrl
    adminEmail = $Email
    storeCode = $StoreCode
    productSku = $ProductSku
    paymentMethodCode = $PaymentMethodCode
    liveStatus = $script:live.status
    readyStatus = $script:ready.status
    databaseStatus = $script:ready.database
    connectionStringSource = $script:ready.connectionStringSource
    metricsDatabaseReady = $script:metrics.database.ready
    requiredTablesPresent = $script:metrics.database.requiredTablesPresent
    salesReadModelCount = $saleCount
    auditEventCount = $auditCount
    dashboardValidation = if ($SkipDashboardBuild) { 'skipped' } else { 'passed' }
    localSecretScan = if ($SkipLocalSecretScan) { 'skipped' } else { 'passed' }
    controlledStoreSetup = 'passed'
    dailyLog = $PilotLogPath
    goNoGo = 'GO'
    message = 'SolidPOS PILOT-01 controlled store pilot setup completed.'
} | Format-List
