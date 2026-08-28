param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [string]$StoreCode = "MAIN",
    [string]$ProductSku = "QSR-AMERICANO",
    [int64]$OpeningAmountCents = 20000,
    [int64]$TenderedCents = 5000,
    [string]$DatabasePath = ".\.runtime\pilot-05-offline-mode-field-test.sqlite",
    [switch]$SkipDashboardValidation
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-05] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString {
    param([securestring]$SecureValue)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Invoke-DbScalar {
    param([Parameter(Mandatory = $true)] [string]$Sql)
    $global:LASTEXITCODE = 0
    $result = docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:16 psql "$DatabaseUrl" -tAc $Sql
    if ($LASTEXITCODE -ne 0) { throw "DB scalar command failed." }
    $global:LASTEXITCODE = 0
    return ($result | Select-Object -First 1).Trim()
}
function Invoke-DbFile {
    param(
        [Parameter(Mandatory = $true)] [string]$SqlPath,
        [Parameter(Mandatory = $true)] [hashtable]$Variables
    )
    $mountDirectory = (Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $fileName = Split-Path -Leaf $SqlPath
    $args = @("run", "--rm", "--env", "DATABASE_URL=$DatabaseUrl", "-v", "${mountDirectory}:/sql:ro", "postgres:16", "psql", "$DatabaseUrl", "-v", "ON_ERROR_STOP=1")
    foreach ($key in $Variables.Keys) { $args += @("-v", "$key=$($Variables[$key])") }
    $args += @("-f", "/sql/$fileName")
    $global:LASTEXITCODE = 0
    $output = docker @args
    if ($LASTEXITCODE -ne 0) { throw "DB file command failed for $SqlPath." }
    $global:LASTEXITCODE = 0
    return $output
}
function Invoke-PosCoreCli {
    param([Parameter(Mandatory = $true)] [string[]]$CliArgs)
    $global:LASTEXITCODE = 0
    & dotnet run --project "src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj" -- @CliArgs
    if ($LASTEXITCODE -ne 0) { throw "PosCore CLI command failed: $($CliArgs -join ' ')" }
    $global:LASTEXITCODE = 0
}
function Get-ResponseItems {
    param($Response)
    if ($null -eq $Response) { return @() }
    if ($Response -is [System.Array]) { return @($Response) }
    if ($null -ne $Response.items) { return @($Response.items) }
    if ($null -ne $Response.data) { return @($Response.data) }
    if ($null -ne $Response.sales) { return @($Response.sales) }
    if ($null -ne $Response.results) { return @($Response.results) }
    if ($null -ne $Response.changes) { return @($Response.changes) }
    return @($Response)
}
function Get-EntityId {
    param($Item)
    if ($null -eq $Item) { return $null }
    if ($null -ne $Item.id) { return [string]$Item.id }
    if ($null -ne $Item.saleId) { return [string]$Item.saleId }
    if ($null -ne $Item.sale_id) { return [string]$Item.sale_id }
    if ($null -ne $Item.receiptId) { return [string]$Item.receiptId }
    if ($null -ne $Item.receipt_id) { return [string]$Item.receipt_id }
    return $null
}
function Get-IntValue {
    param($Object, [string[]]$Names, [int]$Default = 0)
    if ($null -eq $Object) { return $Default }
    foreach ($name in $Names) {
        if ($null -ne $Object.$name) { return [int]$Object.$name }
    }
    return $Default
}
function Find-SaleByLocalSaleId {
    param([string]$LocalSaleId, [string]$StoreId, [string]$TerminalId, [hashtable]$Headers)
    $queries = @(
        "$script:base/api/v1/sales?storeId=$StoreId&terminalId=$TerminalId&status=completed&limit=100",
        "$script:base/api/v1/sales?storeId=$StoreId&terminalId=$TerminalId&limit=100",
        "$script:base/api/v1/sales?limit=100"
    )
    foreach ($attempt in 1..8) {
        foreach ($query in $queries) {
            $response = Invoke-RestMethod -Method Get -Uri $query -Headers $Headers -TimeoutSec 30
            $items = Get-ResponseItems $response
            $match = $items | Where-Object { ([string]$_.localSaleId -eq $LocalSaleId) -or ([string]$_.local_sale_id -eq $LocalSaleId) } | Select-Object -First 1
            if ($null -ne $match) { return $match }
        }
        Start-Sleep -Milliseconds (300 * $attempt)
    }
    throw "Remote sale was not materialized for localSaleId=$LocalSaleId."
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-05-offline-mode-field-test-check.sql"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-05-offline-mode-field-test-log.md"
$runtimeDirectory = Split-Path -Parent $DatabasePath
if ([string]::IsNullOrWhiteSpace($runtimeDirectory)) { $runtimeDirectory = "." }

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-05 validation."
Assert-True (Test-Path $sqlPath) "PILOT-05 SQL validator is missing."
Assert-True ($DatabaseUrl.StartsWith("postgresql://") -or $DatabaseUrl.StartsWith("postgres://")) "DATABASE_URL must be PostgreSQL/Supabase URL."
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
foreach ($suffix in @("", "-wal", "-shm")) { if (Test-Path "$DatabasePath$suffix") { Remove-Item -Force "$DatabasePath$suffix" } }
Write-Step "Local repository guardrails PASS"

Write-Step "Local secret scan..."
$global:LASTEXITCODE = 0
& (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
if (-not $?) { throw "Local secret scan failed." }
$global:LASTEXITCODE = 0
Write-Step "Local secret scan PASS"

if (-not $SkipDashboardValidation) {
    Write-Step "PosDashboard production validation..."
    $global:LASTEXITCODE = 0
    & (Join-Path $repoRoot "scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1") -BaseUrl $script:base -TenantId $TenantId -Email $Email
    if ($LASTEXITCODE -ne 0) { throw "Dashboard validation failed." }
    $global:LASTEXITCODE = 0
    Write-Step "PosDashboard production validation PASS"
}

Write-Step "Production liveness/readiness..."
$live = Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq "alive") "Production liveness did not return alive."
$ready = Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq "ready") "Production readiness did not return ready."
Assert-True ($ready.database -eq "ready") "Production database readiness did not return ready."
Write-Step "Production liveness/readiness PASS"

Write-Step "Admin login..."
$loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
Assert-True (-not [string]::IsNullOrWhiteSpace($session.user.id)) "Admin login did not return user.id."
$adminUserId = [string]$session.user.id
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
Write-Step "Admin login PASS"

Write-Step "Production offline/sync contract lookup..."
$contract = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/contract" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([int]$contract.currentSchemaVersion -eq 4) "Sync contract currentSchemaVersion must be 4."
Assert-True (@($contract.supportedInboundEventTypes) -contains "sale.completed") "Sync contract must support sale.completed."
$storeId = Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' limit 1;"
$productId = Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents = [int64](Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;")
$recipeItemCount = [int](Invoke-DbScalar "select count(*) from pos.recipe_items ri join pos.recipes r on r.tenant_id = ri.tenant_id and r.id = ri.recipe_id join pos.products p on p.tenant_id = r.tenant_id and p.id = r.output_product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and p.status = 'active' and p.deleted_at is null and r.status = 'active' and r.deleted_at is null;")
Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) "Store not found for PILOT-05."
Assert-True (-not [string]::IsNullOrWhiteSpace($productId)) "Product not found for PILOT-05."
Assert-True ($priceCents -gt 0) "Product price not found for PILOT-05."
Assert-True ($recipeItemCount -gt 0) "Product recipe inventory cache contract not found for PILOT-05."
Write-Step "Production offline/sync contract lookup PASS"

Write-Step "Terminal enrollment/register..."
$tokenBody = @{ storeId = $storeId; expiresInMinutes = 30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType "application/json" -Body $tokenBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) "Enrollment token was not returned."
$fingerprint = "pilot-05-offline-$([guid]::NewGuid())"
$terminalBody = @{ enrollmentToken = $enrollment.enrollmentToken; name = "PILOT-05 Offline Mode Field Test Terminal"; fingerprint = $fingerprint; appVersion = "pilot-05" } | ConvertTo-Json
$terminalSession = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/terminal/register" -ContentType "application/json" -Body $terminalBody -TimeoutSec 30
$terminalId = [string]$terminalSession.terminal.id
$terminalAccessToken = [string]$terminalSession.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalId)) "Terminal registration did not return terminal.id."
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalAccessToken)) "Terminal registration did not return accessToken."
$terminalHeaders = @{ Authorization = "Bearer $terminalAccessToken" }
Write-Step "Terminal enrollment/register PASS"

Write-Step "Remote cash shift open for offline reconciliation..."
$openShiftBody = @{ storeId = $storeId; terminalId = $terminalId; openedByUserId = $adminUserId; openingAmountCents = $OpeningAmountCents } | ConvertTo-Json
$remoteShift = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType "application/json" -Body $openShiftBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($remoteShift.id)) "Remote cash shift was not opened."
Write-Step "Remote cash shift open PASS"

Write-Step "PosCore local runtime bootstrap/cache/auth..."
Invoke-PosCoreCli -CliArgs @("init", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("bind", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $storeId, "--terminal-id", $terminalId, "--fingerprint", $fingerprint, "--terminal-token", $terminalAccessToken, "--schema-version", "4")
$bootstrap = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/bootstrap" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([string]$bootstrap.tenantId -eq $TenantId) "Sync bootstrap tenant mismatch."
Assert-True ([string]$bootstrap.storeId -eq $storeId) "Sync bootstrap store mismatch."
Assert-True ([string]$bootstrap.terminalId -eq $terminalId) "Sync bootstrap terminal mismatch."
Invoke-PosCoreCli -CliArgs @("sync-local-user", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $storeId, "--user-id", $adminUserId, "--email", $Email, "--display-name", "Pilot 05 Admin", "--password", $plainPassword, "--role", "admin", "--permissions", "sales.create,sync.push,sync.pull,cash.shift.open,cash.shift.close,receipts.issue", "--max-offline-hours", "72")
Invoke-PosCoreCli -CliArgs @("login-local", "--db", $DatabasePath, "--email", $Email, "--password", $plainPassword)
Invoke-PosCoreCli -CliArgs @("require-permission-local", "--db", $DatabasePath, "--permission", "sales.create")
Invoke-PosCoreCli -CliArgs @("sync-catalog", "--db", $DatabasePath, "--base-url", $script:base, "--access-token", $terminalAccessToken)
Invoke-PosCoreCli -CliArgs @("catalog-status", "--db", $DatabasePath, "--sku", $ProductSku)
Invoke-PosCoreCli -CliArgs @("sync-inventory-cache", "--db", $DatabasePath, "--base-url", $script:base, "--access-token", $terminalAccessToken)
Invoke-PosCoreCli -CliArgs @("inventory-status", "--db", $DatabasePath)
Write-Step "PosCore local runtime bootstrap/cache/auth PASS"

Write-Step "Controlled offline sale while API is not used..."
$localShiftId = [guid]::NewGuid().ToString()
$localSaleId = [guid]::NewGuid().ToString()
$batchId = [guid]::NewGuid().ToString()
$expectedChangeCents = $TenderedCents - $priceCents
Assert-True ($expectedChangeCents -ge 0) "TenderedCents must cover product price."
Invoke-PosCoreCli -CliArgs @("open-local-shift", "--db", $DatabasePath, "--shift-id", $localShiftId, "--opened-by-user-id", $adminUserId, "--opening-amount-cents", $OpeningAmountCents.ToString())
Invoke-PosCoreCli -CliArgs @("sale-offline-from-cache-cash-with-inventory", "--db", $DatabasePath, "--local-sale-id", $localSaleId, "--cashier-user-id", $adminUserId, "--sku", $ProductSku, "--quantity", "1", "--tendered-cents", $TenderedCents.ToString())
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("inventory-status", "--db", $DatabasePath, "--local-sale-id", $localSaleId)
Invoke-PosCoreCli -CliArgs @("cash-status", "--db", $DatabasePath, "--shift-id", $localShiftId)
Write-Step "Controlled offline sale PASS"

Write-Step "Reconnect, push sync, process server batch..."
Invoke-PosCoreCli -CliArgs @("sync-push", "--db", $DatabasePath, "--base-url", $script:base, "--batch-id", $batchId, "--limit", "500", "--terminal-access-token", $terminalAccessToken)
$processBody = @{ batchId = $batchId; maxEvents = 10 } | ConvertTo-Json
$process = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/process" -Headers $terminalHeaders -ContentType "application/json" -Body $processBody -TimeoutSec 30
Assert-True ((Get-IntValue $process @("processedCount")) -ge 1) "Expected at least one processed sync event."
Write-Step "Reconnect, push sync, process server batch PASS"

Write-Step "Idempotency duplicate push validation..."
Invoke-PosCoreCli -CliArgs @("requeue-latest-synced", "--db", $DatabasePath)
$duplicateBatchId = [guid]::NewGuid().ToString()
Invoke-PosCoreCli -CliArgs @("sync-push", "--db", $DatabasePath, "--base-url", $script:base, "--batch-id", $duplicateBatchId, "--limit", "500", "--terminal-access-token", $terminalAccessToken)
$statusAfterDuplicate = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status?storeId=$storeId&terminalId=$terminalId" -Headers $adminHeaders -TimeoutSec 30
Assert-True ((Get-IntValue $statusAfterDuplicate @("deadLetterCount")) -eq 0) "Expected sync deadLetterCount 0 after duplicate push."
Assert-True ((Get-IntValue $statusAfterDuplicate @("rejectedCount")) -eq 0) "Expected sync rejectedCount 0 after duplicate push."
Write-Step "Idempotency duplicate push validation PASS"

Write-Step "Remote sale, receipt, pull sync and local read models..."
$materializedSale = Find-SaleByLocalSaleId -LocalSaleId $localSaleId -StoreId $storeId -TerminalId $terminalId -Headers $adminHeaders
$saleId = Get-EntityId $materializedSale
$saleDetail = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sales/$saleId" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([int64]$saleDetail.totalCents -eq $priceCents) "Remote sale total mismatch."
Assert-True ([int64]$saleDetail.paidCents -eq $priceCents) "Remote sale paid mismatch."
$receiptBody = @{ customerEmail = $null } | ConvertTo-Json
$receipt = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/receipts/$saleId/issue" -Headers $terminalHeaders -ContentType "application/json" -Body $receiptBody -TimeoutSec 30
$receiptId = Get-EntityId $receipt
Assert-True (-not [string]::IsNullOrWhiteSpace($receiptId)) "Receipt issue did not return receipt id."
Invoke-PosCoreCli -CliArgs @("sync-pull", "--db", $DatabasePath, "--base-url", $script:base, "--limit", "100", "--terminal-access-token", $terminalAccessToken)
Invoke-PosCoreCli -CliArgs @("pull-status", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("sync-pull", "--db", $DatabasePath, "--base-url", $script:base, "--limit", "100", "--terminal-access-token", $terminalAccessToken)
Invoke-PosCoreCli -CliArgs @("pull-status", "--db", $DatabasePath)
$tempDirectory = Join-Path $runtimeDirectory "pilot-05-json"
New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
$saleJsonPath = Join-Path $tempDirectory "sale-$saleId.json"
$receiptJsonPath = Join-Path $tempDirectory "receipt-$receiptId.json"
$saleDetail | ConvertTo-Json -Depth 30 | Set-Content -Path $saleJsonPath -Encoding UTF8
$receipt | ConvertTo-Json -Depth 30 | Set-Content -Path $receiptJsonPath -Encoding UTF8
Invoke-PosCoreCli -CliArgs @("save-remote-sale", "--db", $DatabasePath, "--json-file", $saleJsonPath)
Invoke-PosCoreCli -CliArgs @("save-remote-receipt", "--db", $DatabasePath, "--json-file", $receiptJsonPath)
Invoke-PosCoreCli -CliArgs @("readmodel-status", "--db", $DatabasePath, "--local-sale-id", $localSaleId)
Invoke-PosCoreCli -CliArgs @("queue-receipt-print", "--db", $DatabasePath, "--sale-id", $saleId, "--receipt-id", $receiptId, "--receipt-number", ([string]$receipt.receiptNumber))
Invoke-PosCoreCli -CliArgs @("process-print-jobs", "--db", $DatabasePath)
Write-Step "Remote sale, receipt, pull sync and local read models PASS"

Write-Step "Close local/remote cash shift and validate SQL..."
$expectedCashCents = $OpeningAmountCents + $priceCents
Invoke-PosCoreCli -CliArgs @("close-local-shift", "--db", $DatabasePath, "--shift-id", $localShiftId, "--closed-by-user-id", $adminUserId, "--counted-cash-cents", $expectedCashCents.ToString())
$closeShiftBody = @{ closedByUserId = $adminUserId; countedCashCents = $expectedCashCents } | ConvertTo-Json
$closedRemoteShift = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($remoteShift.id)/close" -Headers $terminalHeaders -ContentType "application/json" -Body $closeShiftBody -TimeoutSec 30
$remoteSummary = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($remoteShift.id)/summary" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([int64]$remoteSummary.differenceCents -eq 0) "Remote shift difference must be zero."
$deadLetter = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?terminalId=$terminalId&limit=10" -Headers $adminHeaders -TimeoutSec 30
Assert-True ((Get-ResponseItems $deadLetter).Count -eq 0) "Expected empty dead-letter list."
$sqlOutput = Invoke-DbFile -SqlPath $sqlPath -Variables @{
    tenant_id = $TenantId
    store_id = $storeId
    terminal_id = $terminalId
    batch_id = $batchId
    local_sale_id = $localSaleId
    sale_id = $saleId
    receipt_id = $receiptId
    remote_shift_id = [string]$remoteShift.id
    expected_total_cents = $priceCents
    expected_cash_cents = $expectedCashCents
    counted_cash_cents = $expectedCashCents
    difference_cents = 0
}
$sqlText = ($sqlOutput | Out-String)
Assert-True ($sqlText -match "GO") "PILOT-05 SQL validation did not return GO."
Write-Step "Close local/remote cash shift and validate SQL PASS"

$result = [pscustomobject]@{
    tenantId = $TenantId
    storeId = $storeId
    terminalId = $terminalId
    localDatabasePath = $DatabasePath
    localCashShiftId = $localShiftId
    remoteCashShiftId = [string]$remoteShift.id
    batchId = $batchId
    duplicateBatchId = $duplicateBatchId
    localSaleId = $localSaleId
    remoteSaleId = $saleId
    receiptId = $receiptId
    receiptNumber = [string]$receipt.receiptNumber
    productSku = $ProductSku
    saleTotalCents = [int64]$saleDetail.totalCents
    tenderedCents = $TenderedCents
    changeCents = $expectedChangeCents
    expectedCashCents = [int64]$remoteSummary.expectedCashCents
    countedCashCents = [int64]$remoteSummary.countedCashCents
    differenceCents = [int64]$remoteSummary.differenceCents
    processedCount = (Get-IntValue -Object $process -Names @("processedCount"))
    syncStatusDeadLetterCount = (Get-IntValue -Object $statusAfterDuplicate -Names @("deadLetterCount"))
    deadLetterListCount = (Get-ResponseItems $deadLetter).Count
    schemaVersion = [int]$contract.currentSchemaVersion
    syncContract = "schema_version_4"
    goNoGo = "GO"
    message = "SolidPOS PILOT-05 offline mode field test completed."
}

# Keep the final log writer intentionally simple and ASCII-safe.
# Do not use here-strings, markdown fences, or strings ending in PowerShell backticks.
Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS PILOT-05 Offline Mode Field Test Log"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "Status: PASS REAL PRODUCTION / GO"
Add-Content -Path $logPath -Encoding UTF8 -Value "TenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "StoreId: $storeId"
Add-Content -Path $logPath -Encoding UTF8 -Value "TerminalId: $terminalId"
Add-Content -Path $logPath -Encoding UTF8 -Value "LocalDatabasePath: $DatabasePath"
Add-Content -Path $logPath -Encoding UTF8 -Value "LocalCashShiftId: $localShiftId"
Add-Content -Path $logPath -Encoding UTF8 -Value "RemoteCashShiftId: $($remoteShift.id)"
Add-Content -Path $logPath -Encoding UTF8 -Value "BatchId: $batchId"
Add-Content -Path $logPath -Encoding UTF8 -Value "DuplicateBatchId: $duplicateBatchId"
Add-Content -Path $logPath -Encoding UTF8 -Value "LocalSaleId: $localSaleId"
Add-Content -Path $logPath -Encoding UTF8 -Value "RemoteSaleId: $saleId"
Add-Content -Path $logPath -Encoding UTF8 -Value "ReceiptId: $receiptId"
Add-Content -Path $logPath -Encoding UTF8 -Value "ReceiptNumber: $($receipt.receiptNumber)"
Add-Content -Path $logPath -Encoding UTF8 -Value "ProductSku: $ProductSku"
Add-Content -Path $logPath -Encoding UTF8 -Value "SaleTotalCents: $($saleDetail.totalCents)"
Add-Content -Path $logPath -Encoding UTF8 -Value "TenderedCents: $TenderedCents"
Add-Content -Path $logPath -Encoding UTF8 -Value "ChangeCents: $expectedChangeCents"
Add-Content -Path $logPath -Encoding UTF8 -Value "ExpectedCashCents: $($remoteSummary.expectedCashCents)"
Add-Content -Path $logPath -Encoding UTF8 -Value "CountedCashCents: $($remoteSummary.countedCashCents)"
Add-Content -Path $logPath -Encoding UTF8 -Value "DifferenceCents: $($remoteSummary.differenceCents)"
Add-Content -Path $logPath -Encoding UTF8 -Value "ProcessedCount: $($result.processedCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "SyncStatusDeadLetterCount: $($result.syncStatusDeadLetterCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "DeadLetterListCount: $((Get-ResponseItems $deadLetter).Count)"
Add-Content -Path $logPath -Encoding UTF8 -Value "SchemaVersion: $($contract.currentSchemaVersion)"
Add-Content -Path $logPath -Encoding UTF8 -Value "SyncContract: schema_version_4"
Add-Content -Path $logPath -Encoding UTF8 -Value "GoNoGo: GO"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "SQL validation output:"
Add-Content -Path $logPath -Encoding UTF8 -Value $sqlText

Write-Step "PILOT-05 PASS REAL PRODUCTION / GO"
$result
