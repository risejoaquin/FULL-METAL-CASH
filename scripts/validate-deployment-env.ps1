param(
    [string]$EnvironmentName = $env:ASPNETCORE_ENVIRONMENT
)

$ErrorActionPreference = "Stop"

$required = @(
    "ASPNETCORE_ENVIRONMENT",
    "ASPNETCORE_URLS",
    "Jwt__SigningKey",
    "Jwt__Issuer",
    "Jwt__Audience",
    "AllowedHosts",
    "Cors__AllowedOrigins__0"
)

$missing = @()
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        $missing += $name
    }
}

$postgresConnectionString = [Environment]::GetEnvironmentVariable("ConnectionStrings__Postgres")
$databaseUrl = [Environment]::GetEnvironmentVariable("DATABASE_URL")
if ([string]::IsNullOrWhiteSpace($postgresConnectionString) -and [string]::IsNullOrWhiteSpace($databaseUrl)) {
    $missing += "ConnectionStrings__Postgres or DATABASE_URL"
}

$jwtKey = [Environment]::GetEnvironmentVariable("Jwt__SigningKey")
if (-not [string]::IsNullOrWhiteSpace($jwtKey) -and [Text.Encoding]::UTF8.GetByteCount($jwtKey) -lt 32) {
    throw "Jwt__SigningKey must be at least 32 bytes."
}

$allowedHosts = [Environment]::GetEnvironmentVariable("AllowedHosts")
if ($EnvironmentName -eq "Production" -and ($allowedHosts -eq "*" -or [string]::IsNullOrWhiteSpace($allowedHosts))) {
    throw "AllowedHosts must be explicit in Production."
}

if ($EnvironmentName -eq "Production" -and
    -not $allowedHosts.Contains("healthcheck.railway.app") -and
    -not $allowedHosts.Contains("*.railway.app") -and
    -not $allowedHosts.Contains(".railway.app")) {
    throw "AllowedHosts must include the Railway application host, healthcheck.railway.app, or *.railway.app in Production."
}

if ($missing.Count -gt 0) {
    throw "Missing required environment variables: $($missing -join ', ')"
}

Write-Host "Deployment environment validation passed for $EnvironmentName."
