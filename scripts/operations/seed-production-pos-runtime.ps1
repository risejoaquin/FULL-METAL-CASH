param(
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [string]$Currency = "MXN",
    [string]$DatabaseUrl = $env:DATABASE_URL
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DatabaseUrl)) {
    throw "DATABASE_URL is required. Set `$env:DATABASE_URL or pass -DatabaseUrl."
}

$RootDir = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$SqlFile = Join-Path $RootDir "scripts/operations/seed-production-pos-runtime.sql"

if (-not (Test-Path $SqlFile)) {
    throw "SQL file not found: $SqlFile"
}

docker run --rm `
  --env "DATABASE_URL=$DatabaseUrl" `
  -v "${RootDir}:/work" `
  -w /work `
  postgres:16 `
  psql "$DatabaseUrl" -v "tenant_id=$TenantId" -v "currency=$Currency" -f scripts/operations/seed-production-pos-runtime.sql

if ($LASTEXITCODE -ne 0) {
    throw "Production POS runtime seed failed."
}
