param(
  [Parameter(Mandatory=$true)][string]$TenantId,
  [Parameter(Mandatory=$true)][string]$StoreId,
  [Parameter(Mandatory=$true)][string]$TerminalId,
  [string]$TerminalToken = "AUTO"
)

$ErrorActionPreference = "Stop"

if ($TerminalId -eq "AUTO") {
  $TerminalId = [guid]::NewGuid().ToString()
  Write-Host "Generated local validation TerminalId: $TerminalId"
}

if ($TerminalToken -eq "AUTO") {
  $TerminalToken = "local-resilience-token-" + [guid]::NewGuid().ToString("N")
  Write-Host "Generated local validation TerminalToken."
}

$db = ".\.runtime\poscore-iteration-16.sqlite"
$backupDir = ".\.runtime\backups"
New-Item -ItemType Directory -Force -Path ".\.runtime" | Out-Null
if (Test-Path $db) { Remove-Item $db -Force }

Write-Host "Initializing local PosCore SQLite runtime..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- init --db $db

Write-Host "Binding local terminal for resilience runtime..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- bind --db $db --tenant-id $TenantId --store-id $StoreId --terminal-id $TerminalId --fingerprint "iteration-16-resilience" --terminal-token $TerminalToken --schema-version 4

Write-Host "Running initial local integrity check..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- verify-local-integrity --db $db --strict true

Write-Host "Creating local backup..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- backup-local-db --db $db --backup-dir $backupDir

Write-Host "Seeding controlled recovery fixture..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- seed-resilience-fixture --db $db

Write-Host "Verifying integrity with recoverable warnings..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- verify-local-integrity --db $db

Write-Host "Repairing local runtime with backup..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- repair-local-runtime --db $db --reason "iteration_16_validation" --backup true

Write-Host "Verifying local integrity after repair..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- verify-local-integrity --db $db --strict true

Write-Host "Reading local recovery journal..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- recovery-journal --db $db --limit 5

Write-Host ""
[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $TerminalId
  databasePath = $db
  backupDirectory = $backupDir
  message = "PosCore local resilience/recovery/data integrity completed."
} | Format-List
