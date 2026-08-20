param(
  [string]$TenantId = "AUTO",
  [string]$StoreId = "AUTO",
  [string]$TerminalId = "AUTO"
)

$ErrorActionPreference = "Stop"

Write-Host "Validating PosCore WPF shell project..."

if (-not (Test-Path ".\.runtime")) {
  New-Item -ItemType Directory -Path ".\.runtime" | Out-Null
}

Write-Host "Running WPF shell self-test..."
$LogPath = ".\.runtime\poscore-wpf-shell-self-test.log"
if (Test-Path $LogPath) { Remove-Item $LogPath -Force }

$Output = dotnet run --project src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj -- --self-test 2>&1
$Output | ForEach-Object { Write-Host $_ }

if ($LASTEXITCODE -ne 0) {
  throw "PosCore WPF self-test failed."
}

if (-not (Test-Path $LogPath)) {
  throw "PosCore WPF self-test log was not created: $LogPath"
}

$Output = Get-Content $LogPath
$Output | ForEach-Object { Write-Host $_ }

$Required = @(
  "PosCore WPF self-test started.",
  "WPF shell initialized.",
  "Local login view model ready:",
  "Terminal status view model ready:",
  "Sales view model ready:",
  "Sync status view model ready:",
  "Cash shift view model ready:",
  "PosCore WPF shell validation completed."
)

$Text = ($Output -join "`n")
foreach ($Item in $Required) {
  if (-not $Text.Contains($Item)) {
    throw "Expected WPF shell validation output not found: $Item"
  }
}

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $TerminalId
  wpfProject = "src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj"
  shell = "MVVM"
  views = "Login, Terminal, Sales, Sync, CashShift"
  message = "PosCore WPF shell MVVM foundation completed."
}
