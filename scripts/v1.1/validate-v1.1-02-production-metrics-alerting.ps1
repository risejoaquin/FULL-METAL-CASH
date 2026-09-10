[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$BearerToken,
    [switch]$StaticOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "PASS $Message"
}

function Assert-FileContains([string]$Path, [string]$Pattern, [string]$Message) {
    Assert-True (Test-Path $Path) "required file exists: $Path"
    $content = Get-Content -Raw $Path
    Assert-True ($content -match $Pattern) $Message
}

Write-Host "V1.1-02 Production Metrics & Alerting validation"

$requiredFiles = @(
    "src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PrometheusMetricsFormatter.cs",
    "src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/ProductionAlertEvaluator.cs",
    "deploy/observability/solidpos-v1.1-alert-rules.yml",
    "tests/SolidPOS.PosServer.UnitTests/Observability/ProductionAlertEvaluatorTests.cs",
    "tests/SolidPOS.PosServer.UnitTests/Observability/PrometheusMetricsFormatterTests.cs"
)
foreach ($file in $requiredFiles) {
    Assert-True (Test-Path $file) "required file exists: $file"
}

Assert-FileContains `
    "src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PostgreSqlOperationalMetricsRepository.cs" `
    "COALESCE\(wait_event_type, ''\) <> 'Client'" `
    "PostgreSQL blocker metric excludes client wait_event_type"

Assert-FileContains `
    "src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PrometheusMetricsFormatter.cs" `
    "solidpos_postgresql_active_non_client_wait_event" `
    "Prometheus exports active_non_client_wait_event"

Assert-FileContains `
    "src/PosServer/SolidPOS.PosServer.Infrastructure/Observability/PrometheusMetricsFormatter.cs" `
    "solidpos_financial_sale_payment_mismatch" `
    "Prometheus exports sale/payment mismatch"

$rulesPath = "deploy/observability/solidpos-v1.1-alert-rules.yml"
$rules = Get-Content -Raw $rulesPath
foreach ($requiredRule in @(
    "solidpos_http_error_ratio > 0.05",
    "solidpos_http_p95_latency_ms > 1200",
    "solidpos_postgresql_active_non_client_wait_event > 0",
    "solidpos_sync_dead_letter > 1",
    "solidpos_inventory_negative_stock > 0",
    "solidpos_financial_sale_payment_mismatch > 0"
)) {
    Assert-True ($rules.Contains($requiredRule)) "alert contract preserved: $requiredRule"
}
Assert-True (-not ($rules -match "(?m)^\s*expr:.*solidpos_postgresql_client_read_wait_event")) "ClientRead is not an alert blocker"

$roadmap = Get-Content -Raw "SOLIDPOS_PRODUCT_DEVELOPMENT_ROADMAP_POST_V1_20260828.md"
Assert-True ($roadmap.Contains("schemaVersion = 4")) "schemaVersion 4 roadmap invariant remains present"
Assert-True ($roadmap.Contains("syncContract = schema_version_4")) "sync contract v4 roadmap invariant remains present"

if ($StaticOnly) {
    Write-Host "PASS V1.1-02 PRODUCTION METRICS & ALERTING STATIC CONTRACT"
    exit 0
}

Assert-True (-not [string]::IsNullOrWhiteSpace($BearerToken)) "BearerToken supplied for protected observability endpoints"
$base = $BaseUrl.TrimEnd('/')
$headers = @{ Authorization = "Bearer $BearerToken" }

$live = Invoke-RestMethod -Method Get -Uri "$base/health/live"
Assert-True ($live.status -eq "alive") "health/live is alive"

$ready = Invoke-RestMethod -Method Get -Uri "$base/health/ready"
Assert-True ($ready.status -eq "ready") "health/ready is ready"

$metricsResponse = Invoke-WebRequest -UseBasicParsing -Method Get -Uri "$base/api/v1/observability/prometheus" -Headers $headers
Assert-True ($metricsResponse.StatusCode -eq 200) "protected Prometheus endpoint returns 200"
$metricsText = [string]$metricsResponse.Content
foreach ($metricName in @(
    "solidpos_http_error_ratio",
    "solidpos_http_p95_latency_ms",
    "solidpos_postgresql_active_non_client_wait_event",
    "solidpos_postgresql_client_read_wait_event",
    "solidpos_sync_pending",
    "solidpos_sync_processing",
    "solidpos_sync_retry_pending",
    "solidpos_sync_dead_letter",
    "solidpos_inventory_negative_stock",
    "solidpos_financial_sale_payment_mismatch"
)) {
    Assert-True ($metricsText.Contains($metricName)) "Prometheus metric available: $metricName"
}
Assert-True (-not $metricsText.Contains("tenant_id")) "Prometheus output contains no tenant_id label"

$alerts = Invoke-RestMethod -Method Get -Uri "$base/api/v1/observability/alerts" -Headers $headers
Assert-True ($null -ne $alerts.alerts) "alert evaluation response available"
$dbAlert = @($alerts.alerts | Where-Object { $_.code -eq "postgres_active_non_client_wait_event" })
Assert-True ($dbAlert.Count -eq 1) "database pressure alert contract available"
Assert-True ([string]$dbAlert[0].detail -match "ClientRead") "database pressure alert documents ClientRead as diagnostic only"

Write-Host "PASS V1.1-02 PRODUCTION METRICS & ALERTING"
