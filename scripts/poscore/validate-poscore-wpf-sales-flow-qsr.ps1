param(
  [string]$TenantId = "AUTO",
  [string]$StoreId = "AUTO",
  [string]$TerminalId = "AUTO"
)

$ErrorActionPreference = "Stop"

Write-Host "Validating PosCore WPF QSR sales flow project..."

if (-not (Test-Path ".\.runtime")) {
  New-Item -ItemType Directory -Path ".\.runtime" | Out-Null
}

Write-Host "Running WPF QSR sales flow self-test..."
$LogPath = ".\.runtime\poscore-wpf-sales-flow-qsr-self-test.log"
if (Test-Path $LogPath) { Remove-Item $LogPath -Force }

$Output = dotnet run --project src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj -- --self-test 2>&1
$Output | ForEach-Object { Write-Host $_ }

if ($LASTEXITCODE -ne 0) {
  throw "PosCore WPF QSR sales flow self-test failed."
}

if (-not (Test-Path $LogPath)) {
  throw "PosCore WPF QSR self-test log was not created: $LogPath"
}

$Output = Get-Content $LogPath
$Output | ForEach-Object { Write-Host $_ }

$Required = @(
  "PosCore WPF QSR self-test started.",
  "WPF shell initialized.",
  "Local login view model ready:",
  "Terminal status view model ready:",
  "Catalog view ready:",
  "QSR cart ready:",
  "Cash payment ready:",
  "Receipt print flow ready:",
  "Sync visual state ready:",
  "Cash shift view model ready:",
  "QSR totals: totalCents=4500; tenderedCents=5000; changeCents=500; expectedCashCents=14500",
  "PosCore WPF sales flow QSR validation completed."
)

$Text = ($Output -join "`n")
foreach ($Item in $Required) {
  if (-not $Text.Contains($Item)) {
    throw "Expected WPF QSR validation output not found: $Item"
  }
}

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $TerminalId
  wpfProject = "src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj"
  shell = "MVVM"
  flow = "QSR Sales"
  views = "Login, Terminal, Sales QSR, Sync, CashShift"
  totalCents = 4500
  tenderedCents = 5000
  changeCents = 500
  expectedCashCents = 14500
  message = "PosCore WPF sales flow QSR completed."
}
