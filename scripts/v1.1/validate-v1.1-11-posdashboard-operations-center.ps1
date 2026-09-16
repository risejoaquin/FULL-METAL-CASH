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
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_11_POSDASHBOARD_OPERATIONS_CENTER.md'
$manifestPath = Join-Path $repo 'V1_1_11_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_11_VALIDATION_COMMANDS.md'
$clientPath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\src\api\posServerClient.ts'
$operationsViewPath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\src\features\dashboard\OperationsDashboard.tsx'
$dashboardHomePath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\src\features\dashboard\DashboardHome.tsx'
$selfTestPath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\scripts\self-test.mjs'
$ciWorkflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'
$packageJsonPath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\package.json'
$packageLockPath = Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin\package-lock.json'

# 1. Documentation contracts
Assert-Contains $docPath @(
    'PosDashboard Operations Center',
    'API health',
    'DB health',
    'sync queues',
    'terminal status',
    'incidents',
    'version adoption',
    'alert summary',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $manifestPath @(
    'V1.1-11 PosDashboard Operations Center',
    '5557add2562759ff429a0df2c4fd0785ea1d1b48',
    '322fef5c222c7fa536c7f1faea8f22209e552eeb',
    'schemaVersion: 4',
    'syncContract: schema_version_4'
)

Assert-Contains $commandsPath @(
    'validate-v1.1-11-posdashboard-operations-center.ps1',
    'self-test'
)

# 2. Client contracts & failure isolation
Assert-Contains $clientPath @(
    'ReadinessResponse',
    'ProductionAlertsResponse',
    'TerminalResponse',
    'TerminalDeviceHealthDto',
    'SyncStatusDto',
    'VersionAdoptionSummary',
    'IncidentSummary',
    'getHealthReadiness',
    'getAlerts',
    'getTerminals',
    'hasPermission',
    'PermissionCodes',
    'Promise.allSettled'
)

# 3. Operations dashboard capabilities
Assert-Contains $operationsViewPath @(
    'API health',
    'DB health',
    'Sync queues',
    'Terminal status',
    'Incident summary',
    'Version adoption',
    'Alert summary',
    'hasPermission',
    'terminals.manage',
    'reports.read'
)

# 4. Self-test assertions
Assert-Contains $selfTestPath @(
    '/api/v1/observability/alerts',
    '/api/v1/terminals',
    'ReadinessResponse',
    'ProductionAlertsResponse',
    'TerminalResponse',
    'hasPermission',
    'PermissionCodes',
    'API health',
    'DB health',
    'Sync queues',
    'Terminal status',
    'Incident summary',
    'Version adoption',
    'Alert summary'
)

# 5. CI integration
Assert-Contains $ciWorkflowPath @(
    'actions/setup-node',
    'npm ci',
    'npm run self-test',
    'npm run build',
    'validate-v1.1-11-posdashboard-operations-center.ps1 -StaticOnly'
)

# 6. Invariant: Lockfile present and package.json has zero new dependencies
Assert-True (Test-Path $packageLockPath) "package-lock.json must be present to guarantee deterministic npm ci."
$pkg = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
Assert-True (@($pkg.dependencies.PSObject.Properties).Count -eq 7) "Unexpected direct dependencies added to package.json."
Assert-True (@($pkg.devDependencies.PSObject.Properties).Count -eq 7) "Unexpected devDependencies added to package.json."

# 7. Invariant: No Prometheus scraping in frontend
$clientContent = Get-Content $clientPath -Raw
$opsContent = Get-Content $operationsViewPath -Raw
Assert-True (-not $clientContent.Contains('/observability/prometheus')) "Frontend must not scrape backend Prometheus endpoint."
Assert-True (-not $opsContent.Contains('/observability/prometheus')) "Frontend must not scrape backend Prometheus endpoint."

# 8. Invariant: No DB migration introduced
$migrations = Get-ChildItem -Path (Join-Path $repo 'database\postgresql') -Filter '022_*.sql'
Assert-True ($migrations.Count -eq 0) "Unexpected PostgreSQL migration found: 022_*.sql is not authorized in V1.1-11."

# 9. Invariant: No PosServer changes
$posServerDiff = git diff --name-only HEAD -- src/PosServer
Assert-True ([string]::IsNullOrWhiteSpace($posServerDiff)) "PosServer modifications are not authorized in V1.1-11."

Write-Host 'PASS V1.1-11 POSDASHBOARD OPERATIONS CENTER STATIC CONTRACT'
if ($StaticOnly) { exit 0 }

Write-Host 'Running PosDashboard self-test and build...'
Push-Location (Join-Path $repo 'src\PosDashboard\SolidPOS.PosDashboard.Admin')
try {
    npm run self-test
    if ($LASTEXITCODE -ne 0) { throw "npm run self-test failed." }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
}
finally {
    Pop-Location
}

Write-Host 'PASS V1.1-11 POSDASHBOARD OPERATIONS CENTER FULL VALIDATION'
