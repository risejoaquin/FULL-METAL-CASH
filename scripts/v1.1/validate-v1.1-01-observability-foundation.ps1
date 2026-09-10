param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$BearerToken = "",
    [switch]$SkipDotNet
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Push-Location $repo
try {
    Write-Host "=== V1.1-01 Observability Foundation validator ==="

    if (-not $SkipDotNet) {
        dotnet restore solidpos-platform.sln
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
        dotnet build solidpos-platform.sln --no-restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
        dotnet test solidpos-platform.sln --no-build
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }
    }

    $program = Get-Content .\src\PosServer\SolidPOS.PosServer.Api\Program.cs -Raw
    $correlation = Get-Content .\src\PosServer\SolidPOS.PosServer.Infrastructure\Observability\CorrelationIdMiddleware.cs -Raw
    $requestContext = Get-Content .\src\PosServer\SolidPOS.PosServer.Infrastructure\Observability\RequestLogEnrichmentMiddleware.cs -Raw

    foreach ($required in @('AddSource("Npgsql")', 'WithMetrics', 'AddMeter(SolidPosTelemetry.MeterName)', 'AddConsoleExporter')) {
        if (-not $program.Contains($required)) { throw "Missing OpenTelemetry marker: $required" }
    }
    if (-not $correlation.Contains('X-Correlation-Id') -or -not $correlation.Contains('X-Request-Id')) {
        throw "Correlation/request ID headers are not configured"
    }
    foreach ($forbidden in @('PushProperty("tenant_id"', 'PushProperty("user_id"', 'PushProperty("terminal_id"', 'PushProperty("store_id"')) {
        if ($requestContext.Contains($forbidden)) { throw "Unsafe raw identity telemetry marker found: $forbidden" }
    }

    $correlationId = "v1101-$([Guid]::NewGuid().ToString('N'))"
    $live = Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/health/live" -Headers @{ 'X-Correlation-Id' = $correlationId }
    if ($live.StatusCode -ne 200) { throw "health/live status $($live.StatusCode)" }
    if ($live.Headers['X-Correlation-Id'] -ne $correlationId) { throw "Correlation ID was not preserved" }
    if ([string]::IsNullOrWhiteSpace($live.Headers['X-Request-Id'])) { throw "X-Request-Id missing" }

    $ready = Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/health/ready" -Headers @{ 'X-Correlation-Id' = $correlationId }
    if ($ready.StatusCode -ne 200) { throw "health/ready status $($ready.StatusCode)" }

    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $metrics = Invoke-RestMethod -Uri "$BaseUrl/api/v1/observability/metrics" -Headers @{ Authorization = "Bearer $BearerToken"; 'X-Correlation-Id' = $correlationId }
        if ($null -eq $metrics.requests -or $null -eq $metrics.sync -or $null -eq $metrics.database) {
            throw "Operational metrics response is incomplete"
        }
    } else {
        Write-Host "BearerToken not provided: authenticated /api/v1/observability/metrics probe skipped."
    }

    Write-Host "PASS V1.1-01 OBSERVABILITY FOUNDATION"
}
finally {
    Pop-Location
}
