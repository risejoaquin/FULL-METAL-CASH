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
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_10_CRASH_REPORTING_SAFE_TELEMETRY.md'
$manifestPath = Join-Path $repo 'V1_1_10_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_10_VALIDATION_COMMANDS.md'
$domainModelPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Domain\CrashReport.cs'
$sanitizerPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Diagnostics\CrashReportSanitizer.cs'
$servicePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Diagnostics\CrashReportService.cs'
$wpfAppPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\App.xaml.cs'
$cliProgramPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Cli\Program.cs'
$terminalHealthDtoPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Terminals\TerminalDeviceHealthDto.cs'
$coreTestsPath = Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\Diagnostics\CrashReportingTests.cs'
$ciWorkflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

# 1. Documentation contracts
Assert-Contains $docPath @(
    'Crash Reporting & Safe Telemetry',
    'crash capture',
    'unhandled exception',
    'privacy filtering',
    'opt-in',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $manifestPath @(
    'V1.1-10 Crash Reporting & Safe Telemetry',
    '8f3e2467040e63b820ee2ed0e3a147d07d931db9',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $commandsPath @(
    'validate-v1.1-10-crash-reporting-safe-telemetry.ps1',
    'CrashReportingTests'
)

# 2. Domain model invariants
Assert-Contains $domainModelPath @(
    'CrashReport',
    'CrashId',
    'CorrelationId',
    'SanitizedMessage',
    'SanitizedStackTrace',
    'AuthoritativeSchemaVersion = 4',
    'AuthoritativeSyncContract = "schema_version_4"',
    'TelemetryOptInPolicy',
    'TelemetryOptInMode'
)

# 3. Crash sanitizer
Assert-Contains $sanitizerPath @(
    'CrashReportSanitizer',
    'SanitizeMessage',
    'SanitizeStackTrace',
    '[REDACTED_PASSWORD]',
    '[REDACTED_TOKEN]',
    '[REDACTED_PAN]',
    '[REDACTED_USER]'
)

# 4. Crash report service & retention
Assert-Contains $servicePath @(
    'CrashReportService',
    'PersistCrashReportAsync',
    'PersistCrashReportSynchronous',
    'PruneOldReports',
    'DefaultMaxRetainedReports = 20'
)

# 5. PosCore WPF unhandled exception boundaries
Assert-Contains $wpfAppPath @(
    'DispatcherUnhandledException',
    'AppDomain.CurrentDomain.UnhandledException',
    'TaskScheduler.UnobservedTaskException',
    'RegisterCrashHandlers'
)

# 6. PosCore CLI safety boundary
Assert-Contains $cliProgramPath @(
    'CrashReportService',
    'crashSource: "poscore-cli"'
)

# 7. PosServer optional contract extension
Assert-Contains $terminalHealthDtoPath @(
    'CrashReportEvidenceDto',
    'CrashEvidence',
    'RecentCrashes'
)

# 8. Test coverage
Assert-Contains $coreTestsPath @(
    'Requirement_01_Crash_report_creates_sanitized_structured_envelope',
    'Requirement_03_SchemaVersion_remains_4',
    'Requirement_04_SyncContract_remains_schema_version_4',
    'Requirement_05_Connection_strings_are_redacted',
    'Requirement_06_Bearer_JWT_API_provisioning_secrets_are_redacted',
    'Requirement_07_PAN_like_values_are_redacted',
    'Requirement_08_Windows_user_paths_and_usernames_in_stack_traces_are_sanitized',
    'Requirement_10_Local_crash_persistence_works_offline',
    'Requirement_11_Crash_persistence_failure_does_not_propagate_into_POS_business_operation',
    'Requirement_12_Bounded_retention_removes_oldest_excess_artifacts',
    'Requirement_14_Opt_out_prevents_remote_crash_telemetry_transmission',
    'Requirement_15_Opt_in_allows_eligible_sanitized_evidence'
)

# 9. CI workflow integration
Assert-Contains $ciWorkflowPath @(
    'V1.1-10 crash reporting and safe telemetry static contract',
    'validate-v1.1-10-crash-reporting-safe-telemetry.ps1 -StaticOnly'
)

# 10. Invariant checks: No DB migration introduced
$migrations = Get-ChildItem -Path (Join-Path $repo 'database\postgresql') -Filter '022_*.sql'
Assert-True ($migrations.Count -eq 0) "Unexpected PostgreSQL migration found: 022_*.sql is not authorized in V1.1-10."

# 11. Invariant checks: No raw minidumps
$dumps = Get-ChildItem -Path $repo -Filter '*.dmp' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\\.git\\' }
Assert-True ($null -eq $dumps -or $dumps.Count -eq 0) "Raw minidump files (*.dmp) are forbidden."

Write-Host 'PASS V1.1-10 CRASH REPORTING & SAFE TELEMETRY STATIC CONTRACT'
if ($StaticOnly) { exit 0 }

Write-Host 'Running unit tests for V1.1-10 Crash Reporting & Safe Telemetry...'
dotnet test (Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\SolidPOS.PosCore.UnitTests.csproj') --configuration Release --filter 'FullyQualifiedName~CrashReportingTests' --no-build
if ($LASTEXITCODE -ne 0) { throw "PosCore CrashReportingTests failed." }

dotnet test (Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\SolidPOS.PosServer.UnitTests.csproj') --configuration Release --filter 'FullyQualifiedName~TerminalEnrollmentServiceTests' --no-build
if ($LASTEXITCODE -ne 0) { throw "PosServer TerminalEnrollmentServiceTests failed." }

Write-Host 'PASS V1.1-10 CRASH REPORTING & SAFE TELEMETRY FULL VALIDATION'
