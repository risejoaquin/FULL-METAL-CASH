param(
  [Parameter(Mandatory=$true)][string]$TenantId,
  [Parameter(Mandatory=$true)][string]$StoreId,
  [string]$TerminalId = "AUTO",
  [string]$TerminalToken = "AUTO"
)

$ErrorActionPreference = "Stop"

function Invoke-PosCoreCli {
  param([string[]]$CliArgs)

  dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- @CliArgs
  if ($LASTEXITCODE -ne 0) {
    throw "PosCore CLI command failed: $($CliArgs -join ' ')"
  }
}

if ($TerminalId -eq "AUTO" -or [string]::IsNullOrWhiteSpace($TerminalId)) {
  $TerminalId = [guid]::NewGuid().ToString()
  Write-Host "Generated local validation TerminalId: $TerminalId"
}

if ($TerminalToken -eq "AUTO" -or [string]::IsNullOrWhiteSpace($TerminalToken)) {
  $TerminalToken = "local-hardware-validation-token-$([guid]::NewGuid().ToString('N'))"
  Write-Host "Generated local validation TerminalToken."
}

$db = ".\.runtime\poscore-iteration-13.sqlite"
New-Item -ItemType Directory -Force -Path ".\.runtime" | Out-Null
if (Test-Path $db) { Remove-Item $db -Force }

$saleId = [guid]::NewGuid().ToString()
$receiptId = [guid]::NewGuid().ToString()
$receiptNumber = "SP-HW-$($saleId.Substring(0,8).ToUpperInvariant())"

Write-Host "Initializing local PosCore SQLite runtime..."
Invoke-PosCoreCli @("init", "--db", $db)

Write-Host "Binding local terminal for hardware runtime..."
Invoke-PosCoreCli @(
  "bind", "--db", $db,
  "--tenant-id", $TenantId,
  "--store-id", $StoreId,
  "--terminal-id", $TerminalId,
  "--fingerprint", "iteration-13-hardware-$TenantId",
  "--terminal-token", $TerminalToken,
  "--schema-version", "4"
)

Write-Host "Queueing receipt print job..."
Invoke-PosCoreCli @(
  "queue-receipt-print", "--db", $db,
  "--sale-id", $saleId,
  "--receipt-id", $receiptId,
  "--receipt-number", $receiptNumber,
  "--content", "SolidPOS Iteration 13 fake receipt print validation"
)

Write-Host "Processing receipt print job with fake printer..."
Invoke-PosCoreCli @("process-print-jobs", "--db", $db)

Write-Host "Opening fake cash drawer..."
Invoke-PosCoreCli @("open-cash-drawer-hardware", "--db", $db, "--reason", "iteration_13_validation")

Write-Host "Scanning fake barcode..."
Invoke-PosCoreCli @("scan-barcode", "--db", $db, "--barcode", "QSR-AMERICANO")

Write-Host "Authorizing fake payment terminal..."
Invoke-PosCoreCli @("authorize-payment-terminal", "--db", $db, "--amount-cents", "4500", "--currency", "MXN")

Write-Host "Checking local hardware status..."
Invoke-PosCoreCli @("hardware-status", "--db", $db)

[pscustomobject]@{
  tenantId      = $TenantId
  storeId       = $StoreId
  terminalId    = $TerminalId
  databasePath  = $db
  saleId        = $saleId
  receiptId     = $receiptId
  receiptNumber = $receiptNumber
  message       = "PosCore hardware abstraction runtime completed."
}
