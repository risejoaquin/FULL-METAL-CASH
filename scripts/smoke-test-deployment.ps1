param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [string]$Email = "owner@solidpos.local",
    [string]$Password = "Admin123!",
    [string]$TenantId = "11111111-1111-1111-1111-111111111111"
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")

Write-Host "Checking liveness..."
$live = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/live"
if ($live.status -ne "alive") { throw "Liveness failed: $($live | ConvertTo-Json -Depth 5)" }

Write-Host "Checking readiness..."
$ready = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health/ready"
if ($ready.status -ne "ready" -or $ready.database -ne "ready") { throw "Readiness failed: $($ready | ConvertTo-Json -Depth 5)" }

Write-Host "Checking authenticated metrics..."
$loginBody = @{
    email = $Email
    password = $Password
    tenantId = $TenantId
} | ConvertTo-Json

$session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/auth/login" -ContentType "application/json" -Body $loginBody
if ([string]::IsNullOrWhiteSpace($session.accessToken)) { throw "Login did not return accessToken." }

$metrics = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/v1/observability/metrics" -Headers @{ Authorization = "Bearer $($session.accessToken)" }
if (-not $metrics.database.ready) { throw "Metrics database.ready was false." }
if (-not $metrics.database.requiredTablesPresent) { throw "Metrics requiredTablesPresent was false." }

Write-Host "Deployment smoke test passed for $BaseUrl."
