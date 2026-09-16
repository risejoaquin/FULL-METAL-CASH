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

$docPath = Join-Path $repo 'SOLIDPOS_V1_1_12_V1_1_RELEASE_CLOSURE.md'
$manifestPath = Join-Path $repo 'V1_1_12_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_12_VALIDATION_COMMANDS.md'
$roadmapPath = Join-Path $repo 'SOLIDPOS_PRODUCT_DEVELOPMENT_ROADMAP_POST_V1_20260828.md'
$ciWorkflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'
$packageLockPath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\package-lock.json'
$readinessProbePath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\PostgreSql\PostgreSqlReadinessProbe.cs'
$migrationsPs1Path = Join-Path $repo 'scripts\apply-postgresql-migrations.ps1'
$migrationsShPath = Join-Path $repo 'scripts\apply-postgresql-migrations.sh'
$migration020Path = Join-Path $repo 'database\postgresql\020_postgresql_query_hardening.sql'
$migration021Path = Join-Path $repo 'database\postgresql\021_terminal_device_management_hardening.sql'

Write-Host "Validating V1.1-12 Release Closure static contract..."

# 1. Official Roadmap & Outcome Markers
Assert-Contains $roadmapPath @(
    'V1.1-12 — v1.1 Release Closure',
    'full build/tests',
    'WPF validation',
    'dashboard build',
    'production smoke',
    'capacity regression',
    'sync regression',
    'security regression',
    'release package/provenance',
    'SOLIDPOS_V1_1_PRODUCTION_HARDENING_CLOSED'
)

# 2. Release Closure Documentation Contracts
Assert-Contains $docPath @(
    'SolidPOS V1.1-12: V1.1 Release Closure',
    '8e7dcab48ed94543f081cd2b1394deb11dc4b77a',
    'schemaVersion: 4',
    'syncContract: schema_version_4',
    'full build/tests',
    'WPF validation',
    'dashboard build',
    'production smoke',
    'capacity regression',
    'sync regression',
    'security regression',
    'release package/provenance'
)

Assert-Contains $manifestPath @(
    'V1.1-12 V1.1 Release Closure',
    '8e7dcab48ed94543f081cd2b1394deb11dc4b77a',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $commandsPath @(
    'validate-v1.1-12-v1-1-release-closure.ps1',
    'dotnet test',
    'npm run build'
)

# 3. Architectural Invariants
Assert-Contains $readinessProbePath @(
    'ExpectedSchemaVersion = 4',
    'schema_version_4'
)

# 4. PostgreSQL Migrations Registration
Assert-True (Test-Path $migration020Path) "Migration 020 file missing"
Assert-True (Test-Path $migration021Path) "Migration 021 file missing"
Assert-Contains $migrationsPs1Path @(
    '020_postgresql_query_hardening.sql',
    '021_terminal_device_management_hardening.sql'
)
Assert-Contains $migrationsShPath @(
    '020_postgresql_query_hardening.sql',
    '021_terminal_device_management_hardening.sql'
)

# 5. Prior V1.1 Phase Artifacts & Validators Coverage
$phases = @(
    @{ Id = '01'; Slug = 'observability-foundation' },
    @{ Id = '02'; Slug = 'production-metrics-alerting' },
    @{ Id = '03'; Slug = 'advanced-health-readiness' },
    @{ Id = '04'; Slug = 'postgresql-query-hardening' },
    @{ Id = '05'; Slug = 'poscore-error-ux-recovery' },
    @{ Id = '06'; Slug = 'sync-self-healing' },
    @{ Id = '07'; Slug = 'offline-diagnostics-support-bundle' },
    @{ Id = '08'; Slug = 'terminal-device-management-hardening' },
    @{ Id = '09'; Slug = 'update-channel-velopack-hardening' },
    @{ Id = '10'; Slug = 'crash-reporting-safe-telemetry' },
    @{ Id = '11'; Slug = 'posdashboard-operations-center' }
)

foreach ($phase in $phases) {
    $scriptFilter = "validate-v1.1-$($phase.Id)*.ps1"
    $matchingScripts = Get-ChildItem (Join-Path $repo 'scripts\v1.1') -Filter $scriptFilter
    Assert-True (@($matchingScripts).Count -ge 1) "Validator script missing for phase V1.1-$($phase.Id)"

    $manifestFilter = "V1_1_$($phase.Id)_PACKAGE_MANIFEST.md"
    $matchingManifests = Get-ChildItem $repo -Filter $manifestFilter
    Assert-True (@($matchingManifests).Count -ge 1) "Package manifest missing for phase V1.1-$($phase.Id)"

    $commandsFilter = "V1_1_$($phase.Id)_VALIDATION_COMMANDS.md"
    $matchingCommands = Get-ChildItem $repo -Filter $commandsFilter
    Assert-True (@($matchingCommands).Count -ge 1) "Validation commands missing for phase V1.1-$($phase.Id)"
}

# 6. PosDashboard Deterministic Dependency Invariant
Assert-True (Test-Path $packageLockPath) "PosDashboard package-lock.json missing"
$lockHash = (Get-FileHash $packageLockPath -Algorithm SHA256).Hash
Assert-True ($lockHash -eq 'EBDE214FB907BBF02B317FA7350BBD46DB201606117FA8A2A81181AC93A4AE34') "PosDashboard package-lock.json hash mismatch: $lockHash"

# 7. CI Workflow Inclusion
Assert-Contains $ciWorkflowPath @(
    'validate-v1.1-12-v1-1-release-closure.ps1 -StaticOnly'
)

# 8. WPF Validation Scripts Existence
$wpfSalesFlowScript = Join-Path $repo 'scripts\poscore\validate-poscore-wpf-sales-flow-qsr.ps1'
$wpfShellScript = Join-Path $repo 'scripts\poscore\validate-poscore-wpf-shell.ps1'
Assert-True (Test-Path $wpfSalesFlowScript) "WPF sales flow validation script missing: $wpfSalesFlowScript"
Assert-True (Test-Path $wpfShellScript) "WPF shell validation script missing: $wpfShellScript"

Write-Host "PASS V1.1-12 V1.1 RELEASE CLOSURE STATIC CONTRACT"
