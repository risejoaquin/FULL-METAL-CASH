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
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_08_TERMINAL_DEVICE_MANAGEMENT_HARDENING.md'
$manifestPath = Join-Path $repo 'V1_1_08_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_08_VALIDATION_COMMANDS.md'
$migrationPath = Join-Path $repo 'database\postgresql\021_terminal_device_management_hardening.sql'
$healthDtoPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalDeviceHealthDto.cs'
$configMetaPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalRemoteConfigMetadata.cs'
$detailResponsePath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalDetailResponse.cs'
$assignStoreReqPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\AssignTerminalStoreRequest.cs'
$heartbeatReqPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalHeartbeatRequest.cs'
$heartbeatRespPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalHeartbeatResponse.cs'
$repoInterfacePath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Application\Terminals\ITerminalRepository.cs'
$serviceInterfacePath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Application\Terminals\ITerminalEnrollmentService.cs'
$repoImplPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Terminals\PostgreSqlTerminalRepository.cs'
$serviceImplPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Terminals\TerminalEnrollmentService.cs'
$endpointsPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Endpoints\TerminalEndpoints.cs'
$runtimeEndpointsPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Endpoints\TerminalRuntimeEndpoints.cs'
$middlewareTestsPath = Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\Terminals\TerminalValidationMiddlewareTests.cs'
$serviceTestsPath = Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\Terminals\TerminalEnrollmentServiceTests.cs'
$workflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $docPath @(
    'Terminal & Device Management Hardening',
    'terminal status',
    'last-seen',
    'application/version',
    'store assignment',
    'revoke/disable',
    'device health',
    'remote config metadata',
    'tenant/store isolation',
    'revoked terminal cannot operate improperly',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $manifestPath @(
    'V1.1-08 Terminal & Device Management Hardening',
    'd44eff7db399f2df5452eba5eab99ad79e16a39b',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $commandsPath @(
    'validate-v1.1-08-terminal-device-management-hardening.ps1',
    'TerminalValidationMiddlewareTests',
    'TerminalEnrollmentServiceTests'
)

Assert-Contains $migrationPath @(
    'device_health jsonb',
    'remote_config_metadata jsonb'
)

Assert-Contains $healthDtoPath @(
    'TerminalDeviceHealthDto',
    'BatteryStatus',
    'BatteryLevelPercent',
    'ReportedAtUtc'
)

Assert-Contains $configMetaPath @(
    'TerminalRemoteConfigMetadata',
    'HeartbeatIntervalSeconds',
    'DiagnosticsEnabled',
    'LogLevel',
    'SyncPollingIntervalSeconds',
    'OfflineGracePeriodMinutes'
)

Assert-Contains $detailResponsePath @(
    'TerminalDetailResponse',
    'DeviceHealth',
    'RemoteConfig',
    'HardLockedAt',
    'HardLockReason'
)

Assert-Contains $assignStoreReqPath @(
    'AssignTerminalStoreRequest',
    'StoreId'
)

Assert-Contains $heartbeatReqPath @(
    'TerminalHeartbeatRequest',
    'AppVersion',
    'LocalDbVersion',
    'PendingOutboxCount',
    'LastSyncCursor',
    'DeviceHealth'
)

Assert-Contains $heartbeatRespPath @(
    'TerminalHeartbeatResponse',
    'TerminalId',
    'Status',
    'LastSeenAt',
    'RemoteConfig'
)

Assert-Contains $repoInterfacePath @(
    'GetTerminalAsync',
    'AssignTerminalStoreAsync',
    'DisableTerminalAsync',
    'EnableTerminalAsync',
    'RecordHeartbeatAsync',
    'GetDeviceHealthAsync',
    'GetRemoteConfigAsync',
    'UpdateRemoteConfigAsync'
)

Assert-Contains $serviceInterfacePath @(
    'GetTerminalAsync',
    'AssignStoreAsync',
    'DisableTerminalAsync',
    'EnableTerminalAsync',
    'RecordHeartbeatAsync',
    'GetDeviceHealthAsync',
    'GetRemoteConfigAsync',
    'UpdateRemoteConfigAsync'
)

Assert-Contains $repoImplPath @(
    'GetTerminalAsync',
    'AssignTerminalStoreAsync',
    'DisableTerminalAsync',
    'EnableTerminalAsync',
    'RecordHeartbeatAsync',
    'GetDeviceHealthAsync',
    'GetRemoteConfigAsync',
    'UpdateRemoteConfigAsync'
)

Assert-Contains $serviceImplPath @(
    'GetTerminalAsync',
    'AssignStoreAsync',
    'DisableTerminalAsync',
    'EnableTerminalAsync',
    'RecordHeartbeatAsync',
    'GetDeviceHealthAsync',
    'GetRemoteConfigAsync',
    'UpdateRemoteConfigAsync'
)

Assert-Contains $endpointsPath @(
    'GetTerminal',
    'AssignTerminalStore',
    'DisableTerminal',
    'EnableTerminal',
    'GetTerminalDeviceHealth',
    'GetTerminalRemoteConfig',
    'UpdateTerminalRemoteConfig'
)

Assert-Contains $runtimeEndpointsPath @(
    'RecordTerminalHeartbeat',
    'GetTerminalRuntimeRemoteConfig'
)

Assert-Contains $middlewareTestsPath @(
    'Revoked_terminal_request_is_rejected_with_401_and_inactive_terminal_problem',
    'Disabled_terminal_request_is_rejected_with_401_and_inactive_terminal_problem',
    'Active_terminal_request_passes_validation_and_invokes_next'
)

Assert-Contains $serviceTestsPath @(
    'AssignStore_reassigns_store_and_writes_sync_change',
    'AssignStore_rejects_cross_tenant_store_assignment',
    'Disable_disables_terminal_and_writes_sync_change',
    'Enable_enables_disabled_terminal_and_writes_sync_change',
    'Enable_fails_when_terminal_is_permanently_revoked',
    'RecordHeartbeat_records_activity_health_and_returns_response'
)

Assert-Contains $workflowPath @(
    'V1.1-08 terminal device management hardening static contract',
    'validate-v1.1-08-terminal-device-management-hardening.ps1 -StaticOnly'
)

Write-Host 'PASS V1.1-08 TERMINAL & DEVICE MANAGEMENT HARDENING STATIC CONTRACT'
if ($StaticOnly) { exit 0 }
