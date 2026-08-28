param(
  [string]$DatabasePath = ".\.runtime\poscore-iteration-04.sqlite",
  [Parameter(Mandatory = $true)][string]$TenantId,
  [Parameter(Mandatory = $true)][string]$StoreId,
  [Parameter(Mandatory = $true)][string]$TerminalId,
  [Parameter(Mandatory = $true)][string]$TerminalToken,
  [Parameter(Mandatory = $true)][string]$ProductId
)

$ErrorActionPreference = "Stop"

function Assert-GuidParameter {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Value
  )

  $parsed = [Guid]::Empty
  if (-not [Guid]::TryParse($Value, [ref]$parsed)) {
    throw "$Name must be a valid GUID. Received: '$Value'."
  }
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)][string]$Description,
    [Parameter(Mandatory = $true)][string]$FilePath,
    [Parameter(Mandatory = $true)][string[]]$Arguments
  )

  Write-Host $Description
  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "$Description failed with exit code $LASTEXITCODE."
  }
}

if ($TerminalId -eq "TERMINAL_ID" -or $TerminalId -eq "AUTO" -or [string]::IsNullOrWhiteSpace($TerminalId)) {
  $TerminalId = [Guid]::NewGuid().ToString()
  Write-Host "Generated local validation TerminalId: $TerminalId"
}

if ($TerminalToken -eq "TERMINAL_TOKEN" -or $TerminalToken -eq "AUTO" -or [string]::IsNullOrWhiteSpace($TerminalToken)) {
  $TerminalToken = "local-validation-token-" + ([Guid]::NewGuid().ToString("N"))
  Write-Host "Generated local validation TerminalToken."
}

Assert-GuidParameter -Name "TenantId" -Value $TenantId
Assert-GuidParameter -Name "StoreId" -Value $StoreId
Assert-GuidParameter -Name "TerminalId" -Value $TerminalId
Assert-GuidParameter -Name "ProductId" -Value $ProductId

$runtimeDir = Split-Path -Parent $DatabasePath
if ($runtimeDir -and -not (Test-Path $runtimeDir)) {
  New-Item -ItemType Directory -Path $runtimeDir | Out-Null
}

$projectPath = "src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj"

Invoke-CheckedCommand `
  -Description "Initializing local PosCore SQLite runtime..." `
  -FilePath "dotnet" `
  -Arguments @("run", "--project", $projectPath, "--", "init", "--db", $DatabasePath)

Invoke-CheckedCommand `
  -Description "Binding local terminal..." `
  -FilePath "dotnet" `
  -Arguments @("run", "--project", $projectPath, "--", "bind", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $StoreId, "--terminal-id", $TerminalId, "--fingerprint", "iteration-04-local-$TenantId", "--terminal-token", $TerminalToken, "--schema-version", "4")

Invoke-CheckedCommand `
  -Description "Creating offline sale and local outbox event..." `
  -FilePath "dotnet" `
  -Arguments @("run", "--project", $projectPath, "--", "sale-offline", "--db", $DatabasePath, "--product-id", $ProductId, "--sku", "QSR-AMERICANO", "--name", "Americano 12oz", "--price-cents", "4500", "--quantity", "1", "--currency", "MXN")

Invoke-CheckedCommand `
  -Description "Checking local outbox..." `
  -FilePath "dotnet" `
  -Arguments @("run", "--project", $projectPath, "--", "outbox-status", "--db", $DatabasePath)

Write-Host "PosCore local SQLite runtime validation completed."
Write-Host "TenantId: $TenantId"
Write-Host "StoreId: $StoreId"
Write-Host "TerminalId: $TerminalId"
Write-Host "ProductId: $ProductId"
