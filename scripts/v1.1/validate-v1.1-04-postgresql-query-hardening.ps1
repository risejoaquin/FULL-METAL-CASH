param(
    [string]$BaseUrl = 'http://localhost:5000',
    [string]$TenantId,
    [string]$Email,
    [Security.SecureString]$Password,
    [switch]$StaticOnly,
    [int]$Concurrency = 3,
    [int]$Requests = 6,
    [int]$MaxP95Ms = 1200
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
$resolverPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\PostgreSql\PostgreSqlConnectionStringResolver.cs'
$salesPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Sales\PostgreSqlSalesRepository.cs'
$metricsContractPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Contracts\Observability\OperationalMetricsResponse.cs'
$metricsRepoPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Observability\PostgreSqlOperationalMetricsRepository.cs'
$prometheusPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Observability\PrometheusMetricsFormatter.cs'
$alertsPath = Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Observability\ProductionAlertEvaluator.cs'
$migrationPath = Join-Path $repo 'database\postgresql\020_postgresql_query_hardening.sql'
$testPath = Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\PostgreSqlConnectionHardeningTests.cs'
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_04_POSTGRESQL_CONNECTION_QUERY_HARDENING.md'
$manifestPath = Join-Path $repo 'V1_1_04_PACKAGE_MANIFEST.md'
$workflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $resolverPath @(
    'builder.Pooling = true',
    'PostgreSql:Pool:MaxSize',
    'PostgreSql:Timeouts:ConnectSeconds',
    'PostgreSql:Timeouts:CommandSeconds',
    'ConnectionIdleLifetime',
    'ConnectionPruningInterval',
    'builder.NoResetOnClose = false'
)
Assert-Contains $salesPath @('CommandTimeout = 3', 'ListAsync')
Assert-Contains $metricsContractPath @('IdleInTransactionCount', 'LongRunningQueryCount', 'OldestActiveQueryMs', 'OldestIdleInTransactionMs')
Assert-Contains $metricsRepoPath @("state = 'idle in transaction'", "interval '5 seconds'", "interval '500 milliseconds'", 'pid <> pg_backend_pid()')
Assert-Contains $prometheusPath @('solidpos_postgresql_idle_in_transaction', 'solidpos_postgresql_long_running_query')
Assert-Contains $alertsPath @('postgres_idle_in_transaction', 'postgres_long_running_query')
Assert-Contains $migrationPath @('idx_sales_tenant_terminal_occurred_active', 'idx_sales_tenant_status_occurred_active', 'idx_payments_tenant_status_created')
Assert-Contains $testPath @('Resolver_applies_bounded_pool_and_timeout_policy', 'Resolver_clamps_pool_and_timeout_configuration')
Assert-Contains $docPath @('V1.1-04', 'idle-in-transaction', 'p95 <= 1200 ms')
Assert-Contains $manifestPath @('6bda38f6f922fdb619132650d957a6f7a79b6b3c', 'schemaVersion:', 'syncContract:')
Assert-Contains $workflowPath @('V1.1-04 PostgreSQL query hardening static contract', 'validate-v1.1-04-postgresql-query-hardening.ps1 -StaticOnly')

Write-Host 'PASS V1.1-04 POSTGRESQL QUERY HARDENING STATIC CONTRACT'
if ($StaticOnly) { exit 0 }

Assert-True (-not [string]::IsNullOrWhiteSpace($TenantId)) 'TenantId is required for production validation.'
Assert-True (-not [string]::IsNullOrWhiteSpace($Email)) 'Email is required for production validation.'
Assert-True ($null -ne $Password) 'Password is required for production validation.'

$base = $BaseUrl.TrimEnd('/')
$live = Invoke-RestMethod -Method Get -Uri "$base/health/live" -TimeoutSec 30
$ready = Invoke-RestMethod -Method Get -Uri "$base/health/ready" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Liveness failed.'
Assert-True ($ready.status -in @('ready','degraded')) "Unexpected readiness status: $($ready.status)"
Assert-True ($ready.database -eq 'ready') 'PostgreSQL readiness is not ready.'

$plain = [System.Net.NetworkCredential]::new('', $Password).Password
try {
    $session = Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{
        tenantId = $TenantId
        email = $Email
        password = $plain
    } | ConvertTo-Json) -TimeoutSec 30
} finally {
    $plain = $null
}
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$headers = @{ Authorization = "Bearer $($session.accessToken)" }
$metrics = Invoke-RestMethod -Method Get -Uri "$base/api/v1/observability/metrics" -Headers $headers -TimeoutSec 30

Assert-True ([int]$metrics.database.idleInTransactionCount -eq 0) "Unexpected idle-in-transaction sessions: $($metrics.database.idleInTransactionCount)"
Assert-True ([int]$metrics.database.activeNonClientWaitEventCount -eq 0) "Unexpected active non-client server waits: $($metrics.database.activeNonClientWaitEventCount)"
Assert-True ($null -ne $metrics.database.longRunningQueryCount) 'Slow query visibility metric missing.'
Assert-True ($null -ne $metrics.database.oldestActiveQueryMs) 'Oldest active query metric missing.'

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($null -ne $curl) {
    $jobs = @()
    $results = @()
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
            param($curlPath, $uri)
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
    Assert-True ($results.Count -eq $Requests) 'Capacity gate did not return all samples.'
    Assert-True (@($results | Where-Object { $_.status -ne 200 }).Count -eq 0) 'Capacity gate returned non-200 readiness responses.'
    $latencies = @($results.ms | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling($latencies.Count * 0.95) - 1)
    $p95 = [int]$latencies[$index]
    Assert-True ($p95 -le $MaxP95Ms) "Readiness p95 $p95 ms exceeds approved baseline $MaxP95Ms ms."
    Write-Host "V1.1-04 readiness p95=$p95 ms; samples=$($latencies.Count)"
}

Write-Host 'PASS V1.1-04 POSTGRESQL CONNECTION QUERY HARDENING / GO V1.1-05'
