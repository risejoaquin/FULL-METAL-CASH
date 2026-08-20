param(
  [Parameter(Mandatory=$true)][string]$TenantId,
  [Parameter(Mandatory=$true)][string]$TenantName,
  [Parameter(Mandatory=$true)][string]$AppName,
  [string]$StoreId = "",
  [string]$TerminalId = "AUTO"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$runtimeDir = ".\.runtime"
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
$packagePath = Join-Path $runtimeDir "tenant-branding-package-iteration-17.json"

Write-Host "Creating tenant branding package from PosCore CLI..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- create-branding-package `
  --tenant-id $TenantId `
  --tenant-name $TenantName `
  --app-name $AppName `
  --primary-color "#20242A" `
  --accent-color "#2F80ED" `
  --logo-path "assets/logo-placeholder.png" `
  --receipt-header $TenantName `
  --receipt-footer "Gracias por su compra." `
  --output $packagePath

Write-Host "Validating tenant branding package..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- validate-branding-package --package $packagePath

Write-Host "Showing tenant branding package..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- show-branding-package --package $packagePath

Write-Host "Running PosBuilder WPF branding self-test..."
$builderLogPath = Join-Path $runtimeDir "posbuilder-branding-self-test.log"
if (Test-Path $builderLogPath) { Remove-Item $builderLogPath -Force }

# Hotfix 17.3: execute dotnet directly instead of Start-Process so PowerShell
# receives the real process exit code through $LASTEXITCODE. Start-Process can
# return a completed process with a null/empty ExitCode for WinExe entrypoints,
# even when the self-test completed successfully and wrote the expected log.
& dotnet run --project src/PosBuilder/SolidPOS.PosBuilder.Wpf/SolidPOS.PosBuilder.Wpf.csproj -- --self-test --output $packagePath
$builderExitCode = $LASTEXITCODE

if ($builderExitCode -ne 0) {
  if (Test-Path $builderLogPath) { Get-Content $builderLogPath | ForEach-Object { Write-Host $_ } }
  throw "PosBuilder WPF branding self-test failed with exit code $builderExitCode."
}

if (Test-Path $builderLogPath) {
  Get-Content $builderLogPath | ForEach-Object { Write-Host $_ }
} else {
  throw "PosBuilder WPF branding self-test did not create expected log file: $builderLogPath"
}

Write-Host "Running PosCore WPF branding consumption self-test..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj -- --self-test --branding-package $packagePath

Write-Host ""
[pscustomobject]@{
  tenantId     = $TenantId
  tenantName   = $TenantName
  appName      = $AppName
  storeId      = $StoreId
  terminalId   = $TerminalId
  packagePath  = $packagePath
  posBuilder   = "SolidPOS.PosBuilder.Wpf"
  posCoreWpf   = "SolidPOS.PosCore.Wpf"
  message      = "PosBuilder tenant branding package foundation completed."
} | Format-List
