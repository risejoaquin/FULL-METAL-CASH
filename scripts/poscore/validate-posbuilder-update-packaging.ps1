param(
  [Parameter(Mandatory=$true)][string]$TenantId,
  [Parameter(Mandatory=$true)][string]$TenantName,
  [Parameter(Mandatory=$true)][string]$AppName,
  [string]$StoreId = "",
  [string]$TerminalId = "AUTO",
  [string]$ReleaseVersion = "1.0.0",
  [ValidateSet("stable", "dev")][string]$Channel = "stable"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$runtimeDir = ".\.runtime"
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
$brandingPath = Join-Path $runtimeDir "tenant-branding-package-iteration-18.json"
$updatePackagePath = Join-Path $runtimeDir "solidpos-poscore-$ReleaseVersion-$Channel.pkg"
$updateManifestPath = Join-Path $runtimeDir "solidpos-poscore-update-manifest-iteration-18.json"

Write-Host "Creating tenant branding package for update packaging..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- create-branding-package `
  --tenant-id $TenantId `
  --tenant-name $TenantName `
  --app-name $AppName `
  --primary-color "#20242A" `
  --accent-color "#2F80ED" `
  --logo-path "assets/logo-placeholder.png" `
  --receipt-header $TenantName `
  --receipt-footer "Gracias por su compra." `
  --output $brandingPath

Write-Host "Creating local update package and manifest..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- create-update-package `
  --tenant-id $TenantId `
  --tenant-name $TenantName `
  --app-name $AppName `
  --release-version $ReleaseVersion `
  --channel $Channel `
  --branding-package $brandingPath `
  --output-package $updatePackagePath `
  --output-manifest $updateManifestPath `
  --minimum-poscore-version "1.0.0" `
  --minimum-posbuilder-version "1.0.0" `
  --notes "Iteration 18 local package validation."

Write-Host "Validating update package manifest checksum..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- validate-update-package --manifest $updateManifestPath --package $updatePackagePath

Write-Host "Showing update package manifest..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- show-update-package --manifest $updateManifestPath

Write-Host "Running PosBuilder update packaging self-test..."
dotnet run --project src/PosBuilder/SolidPOS.PosBuilder.Wpf/SolidPOS.PosBuilder.Wpf.csproj -- --self-test `
  --output $brandingPath `
  --update-package $updatePackagePath `
  --update-manifest $updateManifestPath `
  --release-version $ReleaseVersion `
  --channel $Channel

if ($LASTEXITCODE -ne 0) {
  throw "PosBuilder update packaging self-test failed with exit code $LASTEXITCODE."
}

Write-Host "Running PosCore WPF update manifest consumption self-test..."
dotnet run --project src/PosCore/SolidPOS.PosCore.Wpf/SolidPOS.PosCore.Wpf.csproj -- --self-test --branding-package $brandingPath --update-manifest $updateManifestPath

Write-Host ""
[pscustomobject]@{
  tenantId       = $TenantId
  tenantName     = $TenantName
  appName        = $AppName
  storeId        = $StoreId
  terminalId     = $TerminalId
  releaseVersion = $ReleaseVersion
  channel        = $Channel
  brandingPath   = $brandingPath
  packagePath    = $updatePackagePath
  manifestPath   = $updateManifestPath
  posBuilder     = "SolidPOS.PosBuilder.Wpf"
  posCoreWpf     = "SolidPOS.PosCore.Wpf"
  message        = "PosBuilder updates real packaging completed."
} | Format-List
