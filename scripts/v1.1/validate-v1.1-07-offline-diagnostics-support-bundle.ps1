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
        Assert-True ($content.Contains($term)) "Required contract term '$term' missing from $Path"
    }
}

$repo = (Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) '..\..')).Path
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_07_OFFLINE_DIAGNOSTICS_SUPPORT_BUNDLE.md'
$manifestPath = Join-Path $repo 'V1_1_07_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_07_VALIDATION_COMMANDS.md'
$modelsPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Diagnostics\SupportBundleModels.cs'
$servicePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Diagnostics\SupportBundleService.cs'
$cliPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Cli\Program.cs'
$testsPath = Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\SupportBundleServiceTests.cs'
$workflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $docPath @(
    'Diagnostics and Support Bundle Contract',
    'manifest.json',
    'runtime.json',
    'sqlite.json',
    'sync.json',
    'hardware.json',
    'sanitized',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)
Assert-Contains $manifestPath @('V1.1-07 Offline Diagnostics & Support Bundle', '84317853c5b5d1533358ddc897e7f560aea470f1', 'schemaVersion: 4', 'syncContract: schema_version_4')
Assert-Contains $commandsPath @('validate-v1.1-07-offline-diagnostics-support-bundle.ps1', 'SupportBundleServiceTests')
Assert-Contains $modelsPath @('SupportBundleManifest', 'SupportBundleRuntimeInfo', 'SupportBundleSqliteInfo', 'SupportBundleSyncInfo', 'SupportBundleHardwareInfo', 'SupportBundleExportOptions')
Assert-Contains $servicePath @('SupportBundleService', 'SupportBundleSanitizer', 'DefaultSchemaVersion = 4', 'DefaultSyncContract = "schema_version_4"')
Assert-Contains $cliPath @('export-support-bundle', 'SupportBundleService')
Assert-Contains $testsPath @(
    'Bundle_creation_succeeds_and_all_required_files_exist',
    'Manifest_contains_application_version_and_metadata',
    'Runtime_diagnostics_include_app_version_schema_version_and_sync_contract',
    'Sqlite_diagnostics_include_sqlite_state_integrity_and_wal_status',
    'Sync_diagnostics_include_queue_summary_and_last_sync_timestamp',
    'Hardware_diagnostics_include_hardware_state_and_summary',
    'Logs_are_sanitized_and_technical_usefulness_preserved',
    'Sanitizer_redacts_password_like_values',
    'Sanitizer_redacts_bearer_tokens',
    'Sanitizer_redacts_jwt_like_values',
    'Sanitizer_redacts_connection_strings',
    'Bundle_generation_does_not_mutate_operational_data',
    'Bundle_works_offline',
    'Bundle_can_be_generated_without_restarting_poscore'
)
Assert-Contains $workflowPath @('V1.1-07 offline diagnostics support bundle static contract', 'validate-v1.1-07-offline-diagnostics-support-bundle.ps1 -StaticOnly')

Write-Host 'PASS V1.1-07 OFFLINE DIAGNOSTICS & SUPPORT BUNDLE STATIC CONTRACT'
if ($StaticOnly) { exit 0 }
