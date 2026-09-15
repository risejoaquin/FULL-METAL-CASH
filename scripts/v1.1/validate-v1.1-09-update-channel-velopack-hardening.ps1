param(
    [switch]$StaticOnly
)

$ErrorActionPreference = 'Stop'

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Contains([string]$Path, [string[]]$Terms) {
    Assert-True (Test-Path $Path) "Required file missing: $Path"
    $content = Get-Content $Path -Raw
    foreach ($term in $Terms) {
        Assert-True ($content.IndexOf($term, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) "Required contract term '$term' missing from $Path"
    }
}

$repo = (Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) '..\..')).Path
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_09_UPDATE_CHANNEL_VELOPACK_HARDENING.md'
$manifestPath = Join-Path $repo 'V1_1_09_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_09_VALIDATION_COMMANDS.md'
$domainManifestPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Domain\UpdatePackageManifest.cs'
$manifestServicePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Updates\UpdatePackageManifestService.cs'
$rollbackServicePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Updates\RollbackPackageService.cs'
$healthTelemetryPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Updates\UpdateHealthTelemetry.cs'
$terminalHealthDtoPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalDeviceHealthDto.cs'
$posCoreCliPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Cli\Program.cs'
$posBuilderRunnerPath = Join-Path $repo 'src\PosBuilder\SolidPOS.PosBuilder.Wpf\PosBuilderSelfTestRunner.cs'
$coreTestsPath = Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\Updates\UpdateChannelHardeningTests.cs'
$serverTestsPath = Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\BuilderUpdates\StagedRolloutChannelTests.cs'
$ciWorkflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $docPath @(
    'Update Channel & Velopack Hardening',
    'stable',
    'beta',
    'Authenticode',
    'staged rollout',
    'rollback package',
    'update health',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $manifestPath @(
    'V1.1-09 Update Channel & Velopack Hardening',
    '5ca204fad3c3af658e3e97b1f7efee51bc8dc945',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $commandsPath @(
    'validate-v1.1-09-update-channel-velopack-hardening.ps1',
    'UpdateChannelHardeningTests',
    'StagedRolloutChannelTests'
)

Assert-Contains $domainManifestPath @(
    'IsSigned',
    'SigningThumbprint',
    'RollbackVersion',
    'RollbackPackageHash',
    'CurrentManifestVersion = "1.1"'
)

Assert-Contains $manifestServicePath @(
    'SupportedChannels',
    'ProductionChannels',
    'ValidateProductionPolicy',
    'requireSignatureForProduction'
)

Assert-Contains $rollbackServicePath @(
    'RollbackPackageInfo',
    'RollbackPackageValidationResult',
    'RollbackPackageService',
    'SchemaVersion != 4',
    'schema_version_4'
)

Assert-Contains $healthTelemetryPath @(
    'UpdateHealthState',
    'UpdateHealthEvidence',
    'UpdateHealthSanitizer',
    'rollback_triggered',
    'rollback_completed'
)

Assert-Contains $terminalHealthDtoPath @(
    'UpdateHealthEvidenceDto',
    'TerminalDeviceHealthDto',
    'UpdateHealth'
)

Assert-Contains $posCoreCliPath @(
    'create-update-package',
    'validate-update-package',
    '--signed',
    '--signing-thumbprint'
)

Assert-Contains $posBuilderRunnerPath @(
    '--channel',
    '--signed',
    '--signing-thumbprint'
)

Assert-Contains $coreTestsPath @(
    'Validate_accepts_stable_channel',
    'Validate_accepts_beta_channel',
    'Validate_rejects_invalid_channel',
    'Validate_rejects_cross_channel_mismatch',
    'ValidateProductionPolicy_accepts_signed_artifact',
    'ValidateProductionPolicy_rejects_unsigned_production_artifact',
    'RollbackPackageService_accepts_valid_rollback',
    'RollbackPackageService_enforces_schemaVersion_4_invariant',
    'RollbackPackageService_enforces_syncContract_invariant',
    'UpdateHealthSanitizer_masks_sensitive_data'
)

Assert-Contains $serverTestsPath @(
    'Staged_rollout_returns_update_for_targeted_terminal',
    'Staged_rollout_excludes_non_targeted_terminal',
    'Channel_isolation_ensures_stable_request_does_not_receive_beta_release'
)

Assert-Contains $ciWorkflowPath @(
    'V1.1-09 update channel and velopack hardening static contract',
    'validate-v1.1-09-update-channel-velopack-hardening.ps1 -StaticOnly'
)

Write-Host 'PASS V1.1-09 UPDATE CHANNEL & VELOPACK HARDENING STATIC CONTRACT'
if ($StaticOnly) { exit 0 }

Write-Host 'Running unit tests for V1.1-09 Update Channel & Velopack Hardening...'
dotnet test (Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\SolidPOS.PosCore.UnitTests.csproj') --filter 'FullyQualifiedName~UpdateChannelHardeningTests' --no-build
if ($LASTEXITCODE -ne 0) { throw "PosCore UpdateChannelHardeningTests failed." }

dotnet test (Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\SolidPOS.PosServer.UnitTests.csproj') --filter 'FullyQualifiedName~StagedRolloutChannelTests' --no-build
if ($LASTEXITCODE -ne 0) { throw "PosServer StagedRolloutChannelTests failed." }

Write-Host 'PASS V1.1-09 UPDATE CHANNEL & VELOPACK HARDENING FULL VALIDATION'
