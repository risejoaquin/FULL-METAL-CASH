param(
  [string]$BaseUrl = "https://full-metal-cash-production.up.railway.app",
  [string]$TenantId = "0ce5bbd0-528b-4aee-9fe3-93df001a4fde",
  [string]$Email = "admin@micafeteria.com"
)

$ErrorActionPreference = "Stop"
$dashboardPath = "src/PosDashboard/SolidPOS.PosDashboard.Admin"

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory=$true)][string]$FilePath,
    [Parameter(Mandatory=$true)][string[]]$Arguments,
    [Parameter(Mandatory=$true)][string]$FailureMessage
  )

  & $FilePath @Arguments
  $exitCode = $LASTEXITCODE
  if ($null -ne $exitCode -and $exitCode -ne 0) {
    throw "$FailureMessage ExitCode=$exitCode"
  }
}

Write-Host "Validating PosDashboard Admin React foundation..."
if (-not (Test-Path $dashboardPath)) {
  throw "PosDashboard project not found at $dashboardPath"
}

Push-Location $dashboardPath
try {
  Write-Host "Checking Node.js and npm..."
  Invoke-CheckedCommand -FilePath "node" -Arguments @("--version") -FailureMessage "Node.js check failed."
  Invoke-CheckedCommand -FilePath "npm" -Arguments @("--version") -FailureMessage "npm check failed."

  $nodeVersion = node --version
  $npmVersion = npm --version
  Write-Host "Node.js: $nodeVersion"
  Write-Host "npm: $npmVersion"

  Write-Host "Installing dashboard dependencies..."
  Invoke-CheckedCommand -FilePath "npm" -Arguments @("install") -FailureMessage "Dashboard dependency install failed."

  Write-Host "Building dashboard..."
  Invoke-CheckedCommand -FilePath "npm" -Arguments @("run", "build") -FailureMessage "Dashboard build failed."

  Write-Host "Running dashboard self-test..."
  Invoke-CheckedCommand -FilePath "npm" -Arguments @("run", "self-test") -FailureMessage "Dashboard self-test failed."
}
finally {
  Pop-Location
}

Write-Host ""
[pscustomobject]@{
  tenantId   = $TenantId
  baseUrl    = $BaseUrl
  adminEmail = $Email
  project    = $dashboardPath
  stack      = "Vite + React + TypeScript + Tailwind"
  auth       = "/api/v1/auth/login"
  health     = "/health/ready"
  sync       = "/api/v1/sync/status"
  message    = "PosDashboard admin React foundation completed."
} | Format-List
