param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$Email,

    [securestring]$Password,

    [string]$ExpectedDashboardOrigin = '',

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

function Invoke-RequiredStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "[SECURITY] $Name..."
    & $Action
    Write-Host "[SECURITY] $Name PASS"
}

if ($null -eq $Password) {
    $Password = Read-Host -AsSecureString "Admin password for $Email"
}

$plainPassword = Convert-SecureStringToPlainText -Value $Password
if ([string]::IsNullOrWhiteSpace($plainPassword)) {
    throw 'Password cannot be empty.'
}

Invoke-RequiredStep -Name 'Local .gitignore guardrails' -Action {
    $gitignorePath = Join-Path (Get-Location) '.gitignore'
    if (-not (Test-Path $gitignorePath)) { throw '.gitignore is missing.' }
    $gitignore = Get-Content $gitignorePath -Raw
    $requiredRules = @('.env', '.runtime/', 'node_modules/', 'dist/', '*.sqlite', '*.pkg', '*.log')
    foreach ($rule in $requiredRules) {
        if ($gitignore -notlike "*$rule*") { throw ".gitignore is missing required rule: $rule" }
    }
}

if (-not $SkipLocalSecretScan) {
    Invoke-RequiredStep -Name 'Local secret scan' -Action {
        & .\scripts\security\scan-local-secrets.ps1
    }
}

Invoke-RequiredStep -Name 'Production liveness' -Action {
    $script:live = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/live" -TimeoutSec 30
    if ($script:live.status -ne 'alive') { throw "Liveness failed: $($script:live | ConvertTo-Json -Depth 10)" }
}

Invoke-RequiredStep -Name 'Production readiness' -Action {
    $script:ready = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/ready" -TimeoutSec 30
    if ($script:ready.status -ne 'ready' -or $script:ready.database -ne 'ready') {
        throw "Readiness failed: $($script:ready | ConvertTo-Json -Depth 10)"
    }
}

Invoke-RequiredStep -Name 'Admin login with rotated credentials' -Action {
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

Invoke-RequiredStep -Name 'Protected metrics' -Action {
    $script:metrics = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/observability/metrics" -Headers $headers -TimeoutSec 30
    if (-not $script:metrics.database.ready) { throw 'Metrics database.ready was false.' }
    if (-not $script:metrics.database.requiredTablesPresent) { throw 'Metrics database.requiredTablesPresent was false.' }
}

Invoke-RequiredStep -Name 'Sync contract schema' -Action {
    $script:syncContract = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sync/contract" -Headers $headers -TimeoutSec 30
    if ($script:syncContract.currentSchemaVersion -lt 4) {
        throw "Unexpected sync schema version: $($script:syncContract.currentSchemaVersion)"
    }
}

Invoke-RequiredStep -Name 'Sync runtime status' -Action {
    $script:syncStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/sync/status" -Headers $headers -TimeoutSec 30
    if ($null -eq $script:syncStatus) { throw 'Sync status returned null.' }
}

Invoke-RequiredStep -Name 'Provisioning status endpoint' -Action {
    $script:provisioningStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/provisioning/status" -TimeoutSec 30
    if ($null -eq $script:provisioningStatus) { throw 'Provisioning status returned null.' }
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedDashboardOrigin)) {
    Invoke-RequiredStep -Name 'CORS preflight for dashboard origin' -Action {
        $corsHeaders = @{
            Origin = $ExpectedDashboardOrigin
            'Access-Control-Request-Method' = 'GET'
            'Access-Control-Request-Headers' = 'authorization,content-type'
        }
        $response = Invoke-WebRequest -Method Options -Uri "$BaseUrl/api/v1/sync/status" -Headers $corsHeaders -TimeoutSec 30 -SkipHttpErrorCheck
        $allowOrigin = $response.Headers['Access-Control-Allow-Origin']
        if ($allowOrigin -ne $ExpectedDashboardOrigin) {
            throw "CORS origin mismatch. Expected=$ExpectedDashboardOrigin Actual=$allowOrigin Status=$($response.StatusCode)"
        }
    }
}

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
    metricsDatabaseReady = $script:metrics.database.ready
    requiredTablesPresent = $script:metrics.database.requiredTablesPresent
    syncSchemaVersion = $script:syncContract.currentSchemaVersion
    provisioningStatusChecked = $true
    localSecretScan = if ($SkipLocalSecretScan) { 'skipped' } else { 'passed' }
    message = 'SolidPOS production security closure validation completed.'
} | Format-List
