param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [int]$HealthRequests = 24,
    [int]$ProtectedRequests = 18,
    [int]$Concurrency = 4,
    [int]$P95ThresholdMs = 2500,
    [int]$P99ThresholdMs = 5000,
    [int]$MaxErrorPercent = 2,
    [switch]$SkipDashboardBuild,
    [switch]$SkipGa08Revalidation
)

$ErrorActionPreference = 'Stop'
$script:Ga09ValidatorVersion = 'GA-09.4-versioned-httpclient-loadtester-isolation'

function Write-Step([string]$message) { Write-Host "[GA-09] $message" }

function Assert-True($condition, [string]$message) {
    $ok = $false
    if ($null -eq $condition) { $ok = $false }
    elseif ($condition -is [bool]) { $ok = $condition }
    elseif ($condition -is [string]) { $ok = -not [string]::IsNullOrWhiteSpace($condition) -and $condition -notin @('False', 'false', '0') }
    elseif ($condition -is [int] -or $condition -is [long] -or $condition -is [decimal] -or $condition -is [double]) { $ok = ([decimal]$condition -ne 0) }
    elseif ($condition -is [System.Array]) { $ok = ($condition.Count -gt 0) }
    else { $ok = $true }
    if (-not $ok) { throw $message }
}

function Convert-Secret([securestring]$secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Invoke-Checked([string]$name, [scriptblock]$command) {
    $global:LASTEXITCODE = 0
    & $command
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode" }
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [hashtable]$Headers = @{}, [int]$TimeoutSec = 30) {
    $params = @{ Method = $Method; Uri = "$script:base$Path"; Headers = $Headers; TimeoutSec = $TimeoutSec }
    if ($null -ne $Body) {
        $params.Body = $Body | ConvertTo-Json -Depth 30
        $params.ContentType = 'application/json'
    }
    try { return Invoke-RestMethod @params }
    catch {
        $status = ''
        $responseText = ''
        if ($_.Exception.Response) {
            try { $status = "; httpStatus=$([int]$_.Exception.Response.StatusCode)" } catch {}
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = New-Object IO.StreamReader($stream)
                    $responseText = $reader.ReadToEnd()
                    $reader.Close()
                }
            } catch {}
        }
        if (-not [string]::IsNullOrWhiteSpace($responseText)) { $status = "$status; response=$responseText" }
        throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"
    }
}

function Get-HttpStatus([string]$Method, [string]$Path, [hashtable]$Headers = @{}, $Body = $null, [int]$TimeoutSec = 30) {
    try {
        $params = @{ Method = $Method; Uri = "$script:base$Path"; Headers = $Headers; TimeoutSec = $TimeoutSec; UseBasicParsing = $true }
        if ($null -ne $Body) {
            $params.Body = $Body | ConvertTo-Json -Depth 30
            $params.ContentType = 'application/json'
        }
        $response = Invoke-WebRequest @params
        return [int]$response.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        throw
    }
}

function Invoke-DbJsonFile([string]$SqlPath, [hashtable]$Vars) {
    $directory = (Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $file = Split-Path -Leaf $SqlPath
    $args = @('run', '--rm', '--env', "DATABASE_URL=$DatabaseUrl", '-v', "${directory}:/sql:ro", 'postgres:17', 'psql', "$DatabaseUrl", '-tA', '-v', 'ON_ERROR_STOP=1')
    foreach ($key in $Vars.Keys) { $args += @('-v', "$key=$($Vars[$key])") }
    $args += @('-f', "/sql/$file")
    $global:LASTEXITCODE = 0
    $output = docker @args
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 0) { throw "Database SQL failed: $file" }
    for ($i = $output.Count - 1; $i -ge 0; $i--) {
        $line = [string]$output[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { return $line | ConvertFrom-Json } catch {}
    }
    throw "No JSON result returned by $file"
}

function New-Measurement([string]$Name, [string]$Method, [string]$Path, [int]$StatusCode, [long]$LatencyMs, [bool]$Success, [string]$ErrorMessage) {
    [pscustomobject]@{
        name = $Name
        method = $Method
        path = $Path
        statusCode = $StatusCode
        latencyMs = $LatencyMs
        success = $Success
        error = $ErrorMessage
    }
}

function Invoke-HttpMeasurement([string]$Name, [string]$Method, [string]$Path, [hashtable]$Headers = @{}, $Body = $null, [int]$TimeoutSec = 30) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $status = 0
    $success = $false
    $errorMessage = ''
    try {
        $status = Get-HttpStatus $Method $Path $Headers $Body $TimeoutSec
        $success = ($status -ge 200 -and $status -lt 400)
    } catch {
        $errorMessage = $_.Exception.Message
    }
    $watch.Stop()
    return New-Measurement $Name $Method $Path $status ([long]$watch.ElapsedMilliseconds) $success $errorMessage
}

function Get-Percentile([long[]]$Values, [double]$Percentile) {
    if ($null -eq $Values -or $Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    if ($sorted.Count -eq 1) { return [long]$sorted[0] }
    $rank = [Math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1
    if ($rank -lt 0) { $rank = 0 }
    if ($rank -ge $sorted.Count) { $rank = $sorted.Count - 1 }
    return [long]$sorted[$rank]
}

function ConvertTo-Summary([string]$Name, [object[]]$Measurements) {
    $total = @($Measurements).Count
    $successes = @($Measurements | Where-Object { $_.success -eq $true }).Count
    $failures = $total - $successes
    $latencies = @($Measurements | Where-Object { $_.latencyMs -ge 0 } | ForEach-Object { [long]$_.latencyMs })
    $errorPercent = 0
    if ($total -gt 0) { $errorPercent = [Math]::Round(($failures * 100.0) / $total, 2) }
    [pscustomobject]@{
        name = $Name
        totalRequests = $total
        successfulRequests = $successes
        failedRequests = $failures
        errorPercent = $errorPercent
        p50Ms = Get-Percentile $latencies 50
        p95Ms = Get-Percentile $latencies 95
        p99Ms = Get-Percentile $latencies 99
        maxMs = if ($latencies.Count -gt 0) { [long]($latencies | Measure-Object -Maximum).Maximum } else { 0 }
    }
}


function ConvertTo-EndpointBreakdown([string]$GroupName, [object[]]$Measurements) {
    $items = @($Measurements | Where-Object { $null -ne $_ })
    $names = @($items | Select-Object -ExpandProperty name -Unique | Sort-Object)
    $breakdowns = @()
    foreach ($endpointName in $names) {
        $endpointItems = @($items | Where-Object { $_.name -eq $endpointName })
        $summary = ConvertTo-Summary $endpointName $endpointItems
        $statusGroups = @($endpointItems | Group-Object statusCode | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ statusCode = [int]$_.Name; count = $_.Count }
        })
        $sampleFailures = @($endpointItems | Where-Object { $_.success -ne $true } | Select-Object -First 5 | ForEach-Object {
            [pscustomobject]@{ statusCode = $_.statusCode; latencyMs = $_.latencyMs; error = $_.error }
        })
        $breakdowns += [pscustomobject]@{
            group = $GroupName
            endpoint = $endpointName
            summary = $summary
            statusBreakdown = $statusGroups
            sampleFailures = $sampleFailures
        }
    }
    return @($breakdowns)
}

function Write-EndpointBreakdown([string]$GroupName, [object[]]$Measurements) {
    $breakdowns = ConvertTo-EndpointBreakdown $GroupName $Measurements
    foreach ($breakdown in $breakdowns) {
        $statusText = (($breakdown.statusBreakdown | ForEach-Object { "$($_.statusCode):$($_.count)" }) -join ', ')
        if ([string]::IsNullOrWhiteSpace($statusText)) { $statusText = 'none' }
        Write-Step ("$GroupName endpoint=$($breakdown.endpoint) success=$($breakdown.summary.successfulRequests)/$($breakdown.summary.totalRequests) errors=$($breakdown.summary.errorPercent)% p95=$($breakdown.summary.p95Ms)ms p99=$($breakdown.summary.p99Ms)ms statuses=[$statusText]")
        foreach ($failure in @($breakdown.sampleFailures)) {
            $failureText = [string]$failure.error
            if ($failureText.Length -gt 160) { $failureText = $failureText.Substring(0, 160) }
            Write-Step ("$GroupName endpoint=$($breakdown.endpoint) failure status=$($failure.statusCode) latency=$($failure.latencyMs)ms error=$failureText")
        }
    }
    return @($breakdowns)
}

function Invoke-SequentialLoad([string]$Name, [string]$Method, [string]$Path, [int]$Count, [hashtable]$Headers = @{}, $Body = $null) {
    $items = @()
    for ($i = 0; $i -lt $Count; $i++) {
        $items += Invoke-HttpMeasurement $Name $Method $Path $Headers $Body 30
    }
    return $items
}

function Ensure-Ga09HttpClientLoadTester {
    if ('SolidPos.Ga09LoadTesterV094' -as [type]) { return }

    Add-Type -ReferencedAssemblies 'System.Net.Http.dll' -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SolidPos
{
    public sealed class Ga09MeasurementV094
    {
        public string name { get; set; }
        public string method { get; set; }
        public string path { get; set; }
        public int statusCode { get; set; }
        public long latencyMs { get; set; }
        public bool success { get; set; }
        public string error { get; set; }
    }

    public static class Ga09LoadTesterV094
    {
        public static Ga09MeasurementV094[] Run(
            string name,
            string method,
            string url,
            string authorizationHeader,
            string bodyJson,
            int count,
            int concurrency,
            int timeoutSeconds)
        {
            if (count < 1) { count = 1; }
            if (concurrency < 1) { concurrency = 1; }
            if (concurrency > count) { concurrency = count; }

            var results = new Ga09MeasurementV094[count];
            using (var semaphore = new SemaphoreSlim(concurrency))
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                if (!string.IsNullOrWhiteSpace(authorizationHeader))
                {
                    // Authorization is applied per request to avoid shared mutable header state between tasks.
                }

                var tasks = new List<Task>();
                for (var i = 0; i < count; i++)
                {
                    var index = i;
                    tasks.Add(Task.Run(async delegate
                    {
                        await semaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            var measurement = new Ga09MeasurementV094
                            {
                                name = name,
                                method = method,
                                path = url,
                                statusCode = 0,
                                latencyMs = 0,
                                success = false,
                                error = string.Empty
                            };

                            var watch = Stopwatch.StartNew();
                            try
                            {
                                using (var request = new HttpRequestMessage(new HttpMethod(method), url))
                                {
                                    request.Version = new Version(1, 1);
                                    request.Headers.ConnectionClose = false;
                                    if (!string.IsNullOrWhiteSpace(authorizationHeader))
                                    {
                                        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
                                    }
                                    if (!string.IsNullOrWhiteSpace(bodyJson))
                                    {
                                        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                                    }

                                    using (var response = await client.SendAsync(request).ConfigureAwait(false))
                                    {
                                        string responseText = string.Empty;
                                        if (response.Content != null)
                                        {
                                            responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        }
                                        watch.Stop();
                                        measurement.statusCode = (int)response.StatusCode;
                                        measurement.success = measurement.statusCode >= 200 && measurement.statusCode < 400;
                                        if (!measurement.success && !string.IsNullOrWhiteSpace(responseText))
                                        {
                                            responseText = responseText.Replace("\r", " ").Replace("\n", " " );
                                            measurement.error = responseText.Length > 220 ? responseText.Substring(0, 220) : responseText;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                watch.Stop();
                                measurement.error = ex.Message;
                            }
                            finally
                            {
                                measurement.latencyMs = watch.ElapsedMilliseconds;
                                results[index] = measurement;
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray());
            }

            return results;
        }
    }
}
"@
}

function Invoke-ConcurrentLoad([string]$Name, [string]$Method, [string]$Path, [int]$Count, [int]$Concurrency, [hashtable]$Headers = @{}, $Body = $null) {
    Ensure-Ga09HttpClientLoadTester

    $bodyJson = $null
    if ($null -ne $Body) { $bodyJson = $Body | ConvertTo-Json -Depth 30 }

    $authorizationHeader = ''
    if ($Headers.ContainsKey('Authorization')) { $authorizationHeader = [string]$Headers['Authorization'] }

    $items = [SolidPos.Ga09LoadTesterV094]::Run(
        $Name,
        $Method,
        "$script:base$Path",
        $authorizationHeader,
        $bodyJson,
        $Count,
        $Concurrency,
        30)

    return @($items)
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-Secret $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repo = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln = Join-Path $repo 'solidpos-platform.sln'
$sqlPath = Join-Path $scriptRoot 'ga-09-performance-capacity-resilience-offline-readiness-check.sql'
$ga08Script = Join-Path $scriptRoot 'validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1'
$runtime = Join-Path $repo '.runtime\ga-09-performance-capacity-resilience-offline-readiness'
$manifestPath = Join-Path $runtime 'ga-09-performance-capacity-resilience-offline-readiness-manifest.json'
$snapshotPath = Join-Path $runtime 'ga-09-performance-capacity-resilience-offline-readiness-snapshot.json'
$logPath = Join-Path $repo 'docs\ga\logs\ga-09-performance-capacity-resilience-offline-readiness-log.md'
New-Item -ItemType Directory -Force $runtime, (Split-Path $logPath) | Out-Null

Write-Step "Validator version $script:Ga09ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plainPassword)) 'Password secure string resolved to empty/null. Re-run `$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString` before GA-09.'
Assert-True($Concurrency -ge 1 -and $Concurrency -le 16) 'Concurrency must be between 1 and 16 for controlled GA-09 production validation.'
Assert-True($HealthRequests -ge 4 -and $HealthRequests -le 200) 'HealthRequests must be between 4 and 200.'
Assert-True($ProtectedRequests -ge 4 -and $ProtectedRequests -le 120) 'ProtectedRequests must be between 4 and 120.'
Assert-True($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'

Write-Step 'Repository/source guardrails...'
Assert-True(Test-Path $sln) 'solution missing'
Assert-True(Test-Path $sqlPath) 'GA-09 SQL check missing'
Assert-True(Test-Path $ga08Script) 'GA-08 prerequisite validator missing'
Assert-True(Test-Path (Join-Path $repo 'scripts\security\scan-local-secrets.ps1')) 'secret scanner missing'
Assert-True(Test-Path (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Program.cs')) 'PosServer Program.cs missing'
Assert-True(Test-Path (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Endpoints\SyncEndpoints.cs')) 'Sync endpoints missing'
Assert-True(Test-Path (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Endpoints\ObservabilityEndpoints.cs')) 'Observability endpoints missing'

$syncSource = Get-Content -Raw (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Sync\SyncOperationsService.cs')
$programSource = Get-Content -Raw (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Program.cs')
Assert-True($syncSource.Contains('new SyncContractResponse(')) 'Sync contract response source missing'
Assert-True($syncSource.Contains('4,')) 'Sync contract schema version 4 source guard missing'
Assert-True($programSource.Contains('UseRateLimiter')) 'API rate limiter middleware missing'
Assert-True($programSource.Contains('PostgreSqlReadinessProbe')) 'PostgreSQL readiness probe missing'
Write-Step 'Repository/source guardrails PASS'

Write-Step 'Local build/test/secret guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln }
Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore }
Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Invoke-Checked 'secret scan' { & (Join-Path $repo 'scripts\security\scan-local-secrets.ps1') }
if (-not $SkipDashboardBuild.IsPresent) {
    $dashboardScript = Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
    if (Test-Path $dashboardScript) {
        Invoke-Checked 'PosDashboard operations dashboard validation' { & $dashboardScript }
    } else {
        Write-Step 'PosDashboard validation script not found; skipping with condition.'
    }
} else {
    Write-Step 'PosDashboard build skipped by switch.'
}
Write-Step 'Local build/test/secret guardrails PASS'

if (-not $SkipGa08Revalidation.IsPresent) {
    Write-Step 'GA-08 prerequisite revalidation...'
    & $ga08Script `
        -BaseUrl $script:base `
        -TenantId $TenantId `
        -Email $Email `
        -Password $Password `
        -DatabaseUrl $DatabaseUrl `
        -SecretsRotatedAfterExposure `
        -SkipDashboardBuild:$SkipDashboardBuild.IsPresent
    Write-Step 'GA-08 prerequisite revalidation PASS'
} else {
    Write-Step 'GA-08 prerequisite revalidation skipped by switch; GA-09 still requires prior GA-08 PASS evidence in logs.'
}

Write-Step 'Production authentication and baseline protected endpoint checks...'
$login = Invoke-Api Post '/api/v1/auth/login' @{ email = $Email; password = $plainPassword; tenantId = $TenantId }
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.accessToken)) 'Login access token missing'
Assert-True(-not [string]::IsNullOrWhiteSpace([string]$login.refreshToken)) 'Login refresh token missing'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$liveStatus = Get-HttpStatus Get '/health/live'
$readyStatus = Get-HttpStatus Get '/health/ready'
$unauthStatus = Get-HttpStatus Get '/api/v1/observability/metrics'
Assert-True($liveStatus -eq 200) "health/live must return 200; status=$liveStatus"
Assert-True($readyStatus -eq 200) "health/ready must return 200; status=$readyStatus"
Assert-True($unauthStatus -eq 401) "protected observability endpoint without auth must return 401; status=$unauthStatus"
$syncContract = Invoke-Api Get '/api/v1/sync/contract' $null $headers
Assert-True([int]$syncContract.currentSchemaVersion -eq 4) 'Sync contract currentSchemaVersion must remain 4'
$syncStatus = Invoke-Api Get '/api/v1/sync/status' $null $headers
$metrics = Invoke-Api Get '/api/v1/observability/metrics' $null $headers
$now = [DateTimeOffset]::UtcNow
$from = $now.AddDays(-7).ToString('o')
$to = $now.ToString('o')
$salesListStatus = Get-HttpStatus Get ("/api/v1/sales?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20") $headers
$dashboardStatus = Get-HttpStatus Get ("/api/v1/reports/dashboard/overview?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20&trendBucket=day") $headers
Assert-True($salesListStatus -eq 200) "sales list endpoint must return 200; status=$salesListStatus"
Assert-True($dashboardStatus -eq 200) "dashboard overview endpoint must return 200; status=$dashboardStatus"
Write-Step 'Production authentication and baseline protected endpoint checks PASS'

Write-Step 'Database pre-load integrity/capacity snapshot...'
$preSql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId }
Assert-True([int]$preSql.schemaVersion -eq 4) 'Database snapshot schemaVersion must be 4'
Assert-True([string]$preSql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must be schema_version_4'
Assert-True([long]$preSql.rls.rlsMissingTenantTableCount -eq 0) 'RLS missing tenant table count must be 0 before load'
Assert-True([long]$preSql.rls.rlsPolicyMissingTenantTableCount -eq 0) 'RLS policy missing tenant table count must be 0 before load'
Assert-True([long]$preSql.sync.legacySchemaEventCount -eq 0) 'legacySchemaEventCount must be 0 before load'
Assert-True([long]$preSql.sync.retryOverSlaCount -eq 0) 'retryOverSlaCount must be 0 before load'
Assert-True([long]$preSql.financial.duplicateLocalSaleCount -eq 0) 'duplicate local sale count must be 0 before load'
Assert-True([long]$preSql.financial.duplicateLocalPaymentCount -eq 0) 'duplicate local payment count must be 0 before load'
Assert-True([long]$preSql.databasePressure.waitingLockCount -eq 0) 'waiting lock count must be 0 before load'
Write-Step 'Database pre-load integrity/capacity snapshot PASS'

Write-Step 'Controlled load: health/readiness...'
$healthMeasurements = @()
$healthMeasurements += Invoke-ConcurrentLoad 'health-live' Get '/health/live' $HealthRequests $Concurrency
$healthMeasurements += Invoke-ConcurrentLoad 'health-ready' Get '/health/ready' $HealthRequests $Concurrency
$healthSummary = ConvertTo-Summary 'health-readiness-load' $healthMeasurements
$healthBreakdown = Write-EndpointBreakdown 'health-readiness-load' $healthMeasurements
Write-Step "Health load p95=$($healthSummary.p95Ms)ms p99=$($healthSummary.p99Ms)ms errors=$($healthSummary.errorPercent)%"

Write-Step 'Controlled load: protected read endpoints...'
$protectedMeasurements = @()
$protectedMeasurements += Invoke-ConcurrentLoad 'sync-status' Get '/api/v1/sync/status' $ProtectedRequests $Concurrency $headers
$protectedMeasurements += Invoke-ConcurrentLoad 'sync-contract' Get '/api/v1/sync/contract' $ProtectedRequests $Concurrency $headers
$protectedMeasurements += Invoke-ConcurrentLoad 'sales-list' Get ("/api/v1/sales?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20") $ProtectedRequests $Concurrency $headers
$protectedMeasurements += Invoke-ConcurrentLoad 'dashboard-overview' Get ("/api/v1/reports/dashboard/overview?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20&trendBucket=day") $ProtectedRequests $Concurrency $headers
$protectedSummary = ConvertTo-Summary 'protected-read-load' $protectedMeasurements
$protectedBreakdown = Write-EndpointBreakdown 'protected-read-load' $protectedMeasurements
Write-Step "Protected read load p95=$($protectedSummary.p95Ms)ms p99=$($protectedSummary.p99Ms)ms errors=$($protectedSummary.errorPercent)%"

Write-Step 'Controlled resilience/idempotency negative retry checks...'
$invalidSyncBody = @{ batchId = [Guid]::NewGuid(); events = @() }
$invalidStatuses = @()
for ($i = 0; $i -lt 3; $i++) {
    $invalidStatuses += Get-HttpStatus Post '/api/v1/sync/push' $headers $invalidSyncBody
}
foreach ($status in $invalidStatuses) {
    Assert-True(($status -eq 409 -or $status -eq 401 -or $status -eq 403)) "sync negative retry status must be controlled 401/403/409; status=$status"
}
$badSalesStatus = Get-HttpStatus Get '/api/v1/sales?from=not-a-date&limit=20' $headers
Assert-True($badSalesStatus -eq 400) "bad sales filter must return 400; status=$badSalesStatus"
$missingSaleStatus = Get-HttpStatus Get ("/api/v1/sales/$([Guid]::NewGuid())") $headers
Assert-True($missingSaleStatus -eq 404) "missing sale read must return 404; status=$missingSaleStatus"
Write-Step 'Controlled resilience/idempotency negative retry checks PASS'

Write-Step 'Database post-load integrity/capacity snapshot...'
$postSql = Invoke-DbJsonFile $sqlPath @{ tenant_id = $TenantId }
Assert-True([int]$postSql.schemaVersion -eq 4) 'Database snapshot schemaVersion must remain 4 after load'
Assert-True([string]$postSql.syncContract -eq 'schema_version_4') 'Database snapshot syncContract must remain schema_version_4 after load'
Assert-True([long]$postSql.rls.rlsMissingTenantTableCount -eq 0) 'RLS missing tenant table count must remain 0 after load'
Assert-True([long]$postSql.rls.rlsPolicyMissingTenantTableCount -eq 0) 'RLS policy missing tenant table count must remain 0 after load'
Assert-True([long]$postSql.sync.legacySchemaEventCount -eq 0) 'legacySchemaEventCount must remain 0 after load'
Assert-True([long]$postSql.sync.retryOverSlaCount -eq 0) 'retryOverSlaCount must remain 0 after load'
Assert-True([long]$postSql.financial.duplicateLocalSaleCount -eq 0) 'duplicate local sale count must remain 0 after load'
Assert-True([long]$postSql.financial.duplicateLocalPaymentCount -eq 0) 'duplicate local payment count must remain 0 after load'
Assert-True([long]$postSql.databasePressure.waitingLockCount -eq 0) 'waiting lock count must remain 0 after load'
Assert-True([long]$postSql.databasePressure.longRunningQueryCount -eq 0) 'long running query count must remain 0 after load'
Write-Step 'Database post-load integrity/capacity snapshot PASS'

$blockers = @()
$conditions = @()
if ($healthSummary.errorPercent -gt $MaxErrorPercent) { $blockers += "health_error_percent_$($healthSummary.errorPercent)" }
if ($protectedSummary.errorPercent -gt $MaxErrorPercent) { $blockers += "protected_error_percent_$($protectedSummary.errorPercent)" }
if ($healthSummary.p95Ms -gt $P95ThresholdMs) { $blockers += "health_p95_ms_$($healthSummary.p95Ms)" }
if ($protectedSummary.p95Ms -gt $P95ThresholdMs) { $blockers += "protected_p95_ms_$($protectedSummary.p95Ms)" }
if ($healthSummary.p99Ms -gt $P99ThresholdMs) { $blockers += "health_p99_ms_$($healthSummary.p99Ms)" }
if ($protectedSummary.p99Ms -gt $P99ThresholdMs) { $blockers += "protected_p99_ms_$($protectedSummary.p99Ms)" }
if ($SkipDashboardBuild.IsPresent) { $conditions += 'dashboard_build_skipped' }
if ($SkipGa08Revalidation.IsPresent) { $conditions += 'ga08_revalidation_skipped_requires_external_ga08_pass_log' }
if ($blockers.Count -gt 0) { throw "GA-09 BLOCKED: $($blockers -join ', ')" }

$logoutStatus = Get-HttpStatus Post '/api/v1/auth/logout' $headers @{ refreshToken = [string]$login.refreshToken; tenantId = $TenantId }
Assert-True($logoutStatus -eq 204) "GA-09 session logout must return 204; status=$logoutStatus"

$generated = (Get-Date).ToUniversalTime()
$manifest = [ordered]@{
    phase = 'GA-09'
    status = 'PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10'
    tenantId = $TenantId
    baseUrl = $script:base
    generatedAt = $generated.ToString('o')
    validatorVersion = $script:Ga09ValidatorVersion
    entryGate = 'PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09'
    performanceContract = 'ga_performance_capacity_resilience_offline_readiness'
    healthSummary = $healthSummary
    protectedReadSummary = $protectedSummary
    healthEndpointBreakdown = $healthBreakdown
    protectedEndpointBreakdown = $protectedBreakdown
    p95ThresholdMs = $P95ThresholdMs
    p99ThresholdMs = $P99ThresholdMs
    maxErrorPercent = $MaxErrorPercent
    concurrency = $Concurrency
    healthRequestsPerEndpoint = $HealthRequests
    protectedRequestsPerEndpoint = $ProtectedRequests
    liveStatus = $liveStatus
    readyStatus = $readyStatus
    unauthenticatedProtectedEndpointStatus = $unauthStatus
    salesListStatus = $salesListStatus
    dashboardOverviewStatus = $dashboardStatus
    syncNegativeRetryStatuses = $invalidStatuses
    invalidSalesFilterStatus = $badSalesStatus
    missingSaleStatus = $missingSaleStatus
    syncContractCurrentSchemaVersion = [int]$syncContract.currentSchemaVersion
    syncRuntimeStatusTotalEvents = $syncStatus.totalEvents
    preDatabaseSnapshot = $preSql
    postDatabaseSnapshot = $postSql
    blockers = @()
    conditions = $conditions
    schemaVersion = 4
    syncContract = 'schema_version_4'
    generalAvailabilityActivated = $false
    nextPhase = 'GA-10 - Observability, Dashboard, Alerting and On-call Readiness'
}
$manifest | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{
    manifest = $manifest
    preDatabaseSnapshot = $preSql
    postDatabaseSnapshot = $postSql
    healthMeasurements = $healthMeasurements
    protectedMeasurements = $protectedMeasurements
    healthEndpointBreakdown = $healthBreakdown
    protectedEndpointBreakdown = $protectedBreakdown
} | ConvertTo-Json -Depth 40 | Set-Content -Encoding UTF8 $snapshotPath

@"
# GA-09 Performance, Capacity, Resilience and Offline Readiness Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- validatorVersion: $script:Ga09ValidatorVersion
- healthRequestsPerEndpoint: $HealthRequests
- protectedRequestsPerEndpoint: $ProtectedRequests
- concurrency: $Concurrency
- healthP50Ms: $($healthSummary.p50Ms)
- healthP95Ms: $($healthSummary.p95Ms)
- healthP99Ms: $($healthSummary.p99Ms)
- healthErrorPercent: $($healthSummary.errorPercent)
- protectedP50Ms: $($protectedSummary.p50Ms)
- protectedP95Ms: $($protectedSummary.p95Ms)
- protectedP99Ms: $($protectedSummary.p99Ms)
- protectedErrorPercent: $($protectedSummary.errorPercent)
- liveStatus: $liveStatus
- readyStatus: $readyStatus
- unauthenticatedProtectedEndpointStatus: $unauthStatus
- syncContractCurrentSchemaVersion: $($manifest.syncContractCurrentSchemaVersion)
- retryOverSlaCount: $($postSql.sync.retryOverSlaCount)
- legacySchemaEventCount: $($postSql.sync.legacySchemaEventCount)
- duplicateLocalSaleCount: $($postSql.financial.duplicateLocalSaleCount)
- duplicateLocalPaymentCount: $($postSql.financial.duplicateLocalPaymentCount)
- waitingLockCount: $($postSql.databasePressure.waitingLockCount)
- longRunningQueryCount: $($postSql.databasePressure.longRunningQueryCount)
- blockers: {}
- conditions: $($conditions -join ', ')
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath

$plainPassword = $null
[GC]::Collect()
Write-Step 'GA-09 evidence manifest and performance snapshot PASS'
Write-Step 'GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10'
[pscustomobject]$manifest
