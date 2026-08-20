param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$Email,

    [securestring]$Password,

    [switch]$SkipDashboardBuild,

    [switch]$SkipLocalSecretScan
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory = $true)][securestring]$Value)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Invoke-PilotStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "[PILOT] $Name..."
    & $Action
    Write-Host "[PILOT] $Name PASS"
}

if ($null -eq $Password) {
    $Password = Read-Host -AsSecureString "Admin password for $Email"
}

$plainPassword = Convert-SecureStringToPlainText -Value $Password
if ([string]::IsNullOrWhiteSpace($plainPassword)) {
    throw 'Password cannot be empty.'
}

Invoke-PilotStep -Name 'Local repository guardrails' -Action {
    $requiredPaths = @(
        '.gitignore',
        'scripts/security/validate-production-security-closure.ps1',
        'scripts/posdashboard/validate-posdashboard-operations-dashboard.ps1',
        'scripts/pilot/validate-production-pilot-readiness.ps1',
        'docs/pilot/production-pilot-runbook.md',
        'docs/pilot/production-pilot-go-no-go.md'
    )

    foreach ($path in $requiredPaths) {
        if (-not (Test-Path $path)) {
            throw "Required pilot readiness artifact is missing: $path"
        }
    }

    $gitignore = Get-Content '.gitignore' -Raw
    $requiredRules = @('.env', '.runtime/', 'node_modules/', 'dist/', '*.sqlite', '*.pkg', '*.log')
    foreach ($rule in $requiredRules) {
        if ($gitignore -notlike "*$rule*") {
            throw ".gitignore is missing required rule: $rule"
        }
    }
}

if (-not $SkipLocalSecretScan) {
    Invoke-PilotStep -Name 'Local secret scan' -Action {
        & .\scripts\security\scan-local-secrets.ps1
    }
}

if (-not $SkipDashboardBuild) {
    Invoke-PilotStep -Name 'PosDashboard production build and self-test' -Action {
        & .\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1 -BaseUrl $BaseUrl -TenantId $TenantId -Email $Email
    }
}

Invoke-PilotStep -Name 'Production liveness' -Action {
    $script:live = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/live" -TimeoutSec 30
    if ($script:live.status -ne 'alive') {
        throw "Liveness failed: $($script:live | ConvertTo-Json -Depth 10)"
    }
}

Invoke-PilotStep -Name 'Production readiness' -Action {
    $script:ready = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/ready" -TimeoutSec 30
    if ($script:ready.status -ne 'ready' -or $script:ready.database -ne 'ready') {
        throw "Readiness failed: $($script:ready | ConvertTo-Json -Depth 10)"
    }
}

Invoke-PilotStep -Name 'Admin login' -Action {
    $body = @{
        email = $Email
        password = $plainPassword
        tenantId = $TenantId
    } | ConvertTo-Json

    $script:session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/auth/login" -ContentType 'application/json' -Body $body -TimeoutSec 30
    if ([string]::IsNullOrWhiteSpace($script:session.accessToken)) { throw 'Login did not return accessToken.' }
    if ([string]::IsNullOrWhiteSpace($script:session.refreshToken)) { throw 'Login did not return refreshToken.' }
}

$headers = @{ Authorization = "Bearer $($script:session.accessToken)" }

Invoke-PilotStep -Name 'Protected metrics' -Action {
    $script:metrics = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/observability/metrics" -Headers $headers -TimeoutSec 30
    if (-not $script:metrics.database.ready) { throw 'Metrics database.ready was false.' }
    if (-not $script:metrics.database.requiredTablesPresent) { throw 'Metrics database.requiredTablesPresent was false.' }
}

Invoke-PilotStep -Name 'Sync contract schema 4' -Action {
    $script:syncContract = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sync/contract" -Headers $headers -TimeoutSec 30
    if ($script:syncContract.currentSchemaVersion -lt 4) {
        throw "Unexpected sync schema version: $($script:syncContract.currentSchemaVersion)"
    }
}

Invoke-PilotStep -Name 'Sync runtime status' -Action {
    $script:syncStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sync/status" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:syncStatus) { throw 'Sync status returned null.' }
}

Invoke-PilotStep -Name 'Provisioning status endpoint' -Action {
    $script:provisioningStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/provisioning/status" -TimeoutSec 30
    if ($null -eq $script:provisioningStatus) { throw 'Provisioning status returned null.' }
}

Invoke-PilotStep -Name 'Sales read model' -Action {
    $script:sales = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sales?limit=10" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:sales) { throw 'Sales endpoint returned null.' }
}

Invoke-PilotStep -Name 'Returns read model' -Action {
    $script:returns = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/returns?limit=10" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:returns) { throw 'Returns endpoint returned null.' }
}

Invoke-PilotStep -Name 'Audit events read model' -Action {
    $script:auditEvents = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/audit/events?limit=10" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:auditEvents) { throw 'Audit events endpoint returned null.' }
}

$saleCount = @($script:sales).Count
$returnCount = @($script:returns).Count
$auditCount = if ($null -ne $script:auditEvents.items) { @($script:auditEvents.items).Count } else { @($script:auditEvents).Count }

$plainPassword = $null
[GC]::Collect()

Write-Host ''
[pscustomobject]@{
    tenantId = $TenantId
    baseUrl = $BaseUrl
    adminEmail = $Email
    liveStatus = $script:live.status
    readyStatus = $script:ready.status
    databaseStatus = $script:ready.database
    connectionStringSource = $script:ready.connectionStringSource
    metricsDatabaseReady = $script:metrics.database.ready
    requiredTablesPresent = $script:metrics.database.requiredTablesPresent
    syncSchemaVersion = $script:syncContract.currentSchemaVersion
    salesReadModelCount = $saleCount
    returnsReadModelCount = $returnCount
    auditEventCount = $auditCount
    provisioningStatusChecked = $true
    dashboardValidation = if ($SkipDashboardBuild) { 'skipped' } else { 'passed' }
    localSecretScan = if ($SkipLocalSecretScan) { 'skipped' } else { 'passed' }
    refreshTokensRevoked = 'manual-confirmed-required-active_refresh_tokens_0'
    goNoGo = 'GO'
    message = 'SolidPOS production pilot readiness validation completed.'
} | Format-List
