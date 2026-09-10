param(
    [string]$BaseUrl = 'http://localhost:5000',
    [switch]$StaticOnly,
    [int]$Concurrency = 3,
    [int]$Requests = 6,
    [int]$MaxReadinessP95Ms = 1200
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
$probePath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\PostgreSql\PostgreSqlReadinessProbe.cs'
$responsePath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\System\ReadinessResponse.cs'
$programPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Program.cs'
$openApiPath = Join-Path $repo 'contracts\openapi\solidpos-api-v1.openapi.yaml'
$testPath = Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\PostgreSqlReadinessProbeTests.cs'
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_03_ADVANCED_HEALTH_READINESS_DIAGNOSTICS.md'
$manifestPath = Join-Path $repo 'V1_1_03_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_03_VALIDATION_COMMANDS.md'
$workflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $probePath @(
    'ExpectedSchemaVersion = 4',
    'ExpectedSyncContract = "schema_version_4"',
    'DatabaseLatencyDegradedThresholdMs = 1200',
    'information_schema.columns',
    'sync_inbox_events',
    'sync_conflicts',
    'RUNTIME_STORAGE_WRITE_FAILED',
    'PostgreSQL readiness check failed.'
)
Assert-Contains $responsePath @('DatabaseLatencyMs', 'SchemaCompatibility', 'SyncReadiness', 'StorageReadiness', 'ReadinessDependencyResponse')
Assert-Contains $programPath @('readiness.DatabaseLatencyMs', 'readiness.Dependencies', 'readiness.IsAvailable')
Assert-Contains $openApiPath @('ReadinessDependencyResponse:', 'databaseLatencyMs:', 'schemaCompatibility:', 'syncReadiness:', 'storageReadiness:', '- unavailable')
Assert-Contains $testPath @('Missing_configuration_returns_unavailable_dependency_breakdown', 'Expected_schema_and_sync_contract_remain_version_four')
Assert-Contains $docPath @('V1.1-03', 'Advanced Health / Readiness Diagnostics', 'schemaVersion 4', 'schema_version_4')
Assert-Contains $manifestPath @('09ea409f9c94b8fd793df2fd2aced47cfaec4e1a', 'schemaVersion = 4', 'syncContract = schema_version_4')
Assert-Contains $commandsPath @('validate-v1.1-03-advanced-health-readiness.ps1', 'MaxReadinessP95Ms 1200')
Assert-Contains $workflowPath @('V1.1-03 advanced health readiness static contract', 'validate-v1.1-03-advanced-health-readiness.ps1 -StaticOnly')

$probeContent = Get-Content $probePath -Raw
Assert-True (-not $probeContent.Contains('ex.Message')) 'Readiness diagnostics must not expose exception messages.'
Assert-True (-not $probeContent.Contains('_connectionStringResolution.ErrorMessage')) 'Readiness diagnostics must not expose raw connection-string resolver errors.'

Write-Host 'PASS V1.1-03 ADVANCED HEALTH READINESS STATIC CONTRACT'
if ($StaticOnly) { exit 0 }

$base = $BaseUrl.TrimEnd('/')
$ready = Invoke-RestMethod -Method Get -Uri "$base/health/ready"
Assert-True ($ready.status -in @('ready','degraded')) "Unexpected readiness status: $($ready.status)"
Assert-True ($ready.database -eq 'ready') "Database is not ready: $($ready.database)"
Assert-True ([int]$ready.schemaVersion -eq 4) 'schemaVersion must remain 4.'
Assert-True ($ready.syncContract -eq 'schema_version_4') 'syncContract must remain schema_version_4.'
Assert-True ($ready.schemaCompatibility -eq 'compatible') 'Runtime schema compatibility must be compatible.'
Assert-True ($ready.syncReadiness -eq 'ready') 'Sync readiness must be ready.'
Assert-True ($ready.storageReadiness -in @('ready','degraded')) 'Storage readiness state missing or invalid.'
Assert-True ($null -ne $ready.databaseLatencyMs -and [long]$ready.databaseLatencyMs -ge 0) 'Database latency diagnostic missing.'
$dependencyNames = @($ready.dependencies | ForEach-Object { [string]$_.name })
foreach ($required in @('database','schema','sync','storage')) {
    Assert-True ($dependencyNames -contains $required) "Missing readiness dependency: $required"
}

$serialized = $ready | ConvertTo-Json -Depth 20
foreach ($forbidden in @('Password=','Username=','Host=','postgres://','postgresql://')) {
    Assert-True (-not $serialized.Contains($forbidden)) "Sensitive database configuration leaked in readiness diagnostics: $forbidden"
}

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($null -ne $curl) {
    $jobs = @(); $results = @()
    for ($i = 0; $i -lt $Requests; $i++) {
        while (@($jobs | Where-Object { $_.State -eq 'Running' }).Count -ge $Concurrency) {
            [void](Wait-Job -Job @($jobs) -Any -Timeout 1)
            foreach ($job in @($jobs | Where-Object { $_.State -ne 'Running' })) {
                $results += @(Receive-Job $job -ErrorAction SilentlyContinue)
                Remove-Job $job -Force -ErrorAction SilentlyContinue
                $jobs = @($jobs | Where-Object { $_.Id -ne $job.Id })
            }
        }
        $jobs += Start-Job -ScriptBlock {
            param($curlPath,$uri)
            $line = & $curlPath '-sS' '-o' 'NUL' '--max-time' '20' '-w' '__SOLIDPOS__ %{http_code} %{time_total}' $uri 2>&1 | Select-Object -Last 1
            if ($line -match '^__SOLIDPOS__\s+(\d{3})\s+([0-9.]+)$') {
                [pscustomobject]@{ status=[int]$Matches[1]; ms=[int][Math]::Round(([double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture))*1000) }
            } else {
                [pscustomobject]@{ status=0; ms=0 }
            }
        } -ArgumentList @($curl.Source, "$base/health/ready")
    }
    while ($jobs.Count -gt 0) {
        [void](Wait-Job -Job @($jobs) -Any -Timeout 1)
        foreach ($job in @($jobs | Where-Object { $_.State -ne 'Running' })) {
            $results += @(Receive-Job $job -ErrorAction SilentlyContinue)
            Remove-Job $job -Force -ErrorAction SilentlyContinue
            $jobs = @($jobs | Where-Object { $_.Id -ne $job.Id })
        }
    }
    $success = @($results | Where-Object { $_.status -eq 200 })
    Assert-True ($success.Count -eq $Requests) "Readiness capacity gate failed: $($success.Count)/$Requests HTTP 200."
    $latencies = @($success | ForEach-Object { [int]$_.ms } | Sort-Object)
    $index = [Math]::Ceiling($latencies.Count * 0.95) - 1
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $latencies.Count) { $index = $latencies.Count - 1 }
    $p95 = [int]$latencies[$index]
    Assert-True ($p95 -le $MaxReadinessP95Ms) "Readiness p95 regression: ${p95}ms > ${MaxReadinessP95Ms}ms."
    Write-Host "PASS readiness capacity gate concurrency=$Concurrency requests=$Requests p95=${p95}ms"
} else {
    Write-Host 'WARN curl.exe unavailable; capacity gate not executed by this validator.'
}

Write-Host 'PASS V1.1-03 ADVANCED HEALTH / READINESS DIAGNOSTICS'
