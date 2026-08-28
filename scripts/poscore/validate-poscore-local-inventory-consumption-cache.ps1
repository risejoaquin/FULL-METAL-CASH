param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$StoreId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$DatabasePath = ".\.runtime\poscore-iteration-09.sqlite",
    [string]$Sku = "QSR-AMERICANO",
    [int]$ExpectedPriceCents = 4500,
    [int]$OpeningAmountCents = 10000
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
    throw "DATABASE_URL is required for remote inventory ledger reconciliation."
}

$runtimeDirectory = Split-Path $DatabasePath -Parent
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
if (Test-Path $DatabasePath) { Remove-Item -Force $DatabasePath }
if (Test-Path "$DatabasePath-wal") { Remove-Item -Force "$DatabasePath-wal" }
if (Test-Path "$DatabasePath-shm") { Remove-Item -Force "$DatabasePath-shm" }

function Invoke-PosCoreCli {
    param([Parameter(Mandatory = $true)] [string[]]$CliArgs)

    dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- @CliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "PosCore CLI command failed: $($CliArgs -join ' ')"
    }
}

function Invoke-RemoteScalarSql {
    param([Parameter(Mandatory = $true)] [string]$Sql)

    $value = docker run --rm `
      --env "DATABASE_URL=$env:DATABASE_URL" `
      postgres:16 `
      psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -t -A -c $Sql

    if ($LASTEXITCODE -ne 0) {
        throw "Remote SQL scalar query failed."
    }

    return ($value | Select-Object -Last 1).Trim()
}

Write-Host "Logging in as production admin..."
$loginBody = @{
  email = $AdminEmail
  password = $AdminPassword
  tenantId = $TenantId
} | ConvertTo-Json

$adminSession = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/auth/login" `
  -ContentType "application/json" `
  -Body $loginBody

$adminUserId = $adminSession.user.id
if ([string]::IsNullOrWhiteSpace($adminUserId)) {
    throw "Admin auth response did not include user.id."
}

$adminHeaders = @{ Authorization = "Bearer $($adminSession.accessToken)" }

Write-Host "Creating terminal enrollment token..."
$tokenBody = @{
  storeId = $StoreId
  expiresInMinutes = 30
} | ConvertTo-Json

$enrollment = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/terminals/enrollment-token" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body $tokenBody

$fingerprint = "iteration-09-poscore-inventory-cache-$([guid]::NewGuid())"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 09 PosCore Inventory Cache Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-09"
} | ConvertTo-Json

Write-Host "Registering remote terminal session..."
$terminalSession = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/auth/terminal/register" `
  -ContentType "application/json" `
  -Body $terminalBody

$terminalId = $terminalSession.terminal.id
$terminalAccessToken = $terminalSession.accessToken
$terminalHeaders = @{ Authorization = "Bearer $terminalAccessToken" }

Write-Host "Initializing local PosCore SQLite runtime..."
Invoke-PosCoreCli -CliArgs @("init", "--db", $DatabasePath)

Write-Host "Binding local terminal to remote terminal session..."
Invoke-PosCoreCli -CliArgs @(
  "bind", "--db", $DatabasePath,
  "--tenant-id", $TenantId,
  "--store-id", $StoreId,
  "--terminal-id", $terminalId,
  "--fingerprint", $fingerprint,
  "--terminal-token", $terminalAccessToken,
  "--schema-version", "4"
)

Write-Host "Ensuring production inventory recipe/BOM seed exists for Iteration 09..."
$seedPath = "scripts/operations/seed-production-inventory-runtime.sql"
if (!(Test-Path $seedPath)) {
    throw "Inventory runtime seed script not found: $seedPath"
}

docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  -v "${PWD}:/work" `
  -w /work `
  postgres:16 `
  psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -v tenant_id="$TenantId" -f $seedPath

if ($LASTEXITCODE -ne 0) {
    throw "Production inventory runtime seed failed."
}

Write-Host "Syncing remote catalog into local SQLite cache..."
Invoke-PosCoreCli -CliArgs @(
  "sync-catalog", "--db", $DatabasePath,
  "--base-url", $base,
  "--access-token", $terminalAccessToken
)

Write-Host "Syncing remote inventory recipes into local SQLite cache..."
Invoke-PosCoreCli -CliArgs @(
  "sync-inventory-cache", "--db", $DatabasePath,
  "--base-url", $base,
  "--access-token", $terminalAccessToken
)

Write-Host "Checking cached product and inventory recipe cache..."
Invoke-PosCoreCli -CliArgs @("catalog-status", "--db", $DatabasePath, "--sku", $Sku)
Invoke-PosCoreCli -CliArgs @("inventory-status", "--db", $DatabasePath)

Write-Host "Opening remote cash shift for inventory-cache offline sale..."
$openShiftBody = @{
  storeId = $StoreId
  terminalId = $terminalId
  openedByUserId = $adminUserId
  openingAmountCents = $OpeningAmountCents
} | ConvertTo-Json

$shift = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $openShiftBody

$batchId = [guid]::NewGuid().ToString()
$localSaleId = [guid]::NewGuid().ToString()

Write-Host "Creating offline sale from cached catalog SKU with local inventory consumption..."
Invoke-PosCoreCli -CliArgs @(
  "sale-offline-from-cache-with-inventory", "--db", $DatabasePath,
  "--local-sale-id", $localSaleId,
  "--cashier-user-id", $adminUserId,
  "--sku", $Sku,
  "--quantity", "1"
)

Write-Host "Local inventory movements before remote push..."
Invoke-PosCoreCli -CliArgs @("inventory-status", "--db", $DatabasePath, "--local-sale-id", $localSaleId)

Write-Host "Local outbox before remote push..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

Write-Host "Pushing inventory-cache sale.completed event to PosServer..."
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $batchId,
  "--limit", "500"
)

Write-Host "Processing inventory-cache sale.completed remote batch..."
$processBody = @{
  batchId = $batchId
  maxEvents = 10
} | ConvertTo-Json
$process = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/sync/process" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $processBody

if ($process.processedCount -lt 1) {
    $serializedProcess = $process | ConvertTo-Json -Depth 20
    throw "Expected inventory-cache sale.completed processing to process one event. Response: $serializedProcess"
}

Write-Host "Looking up materialized remote sale by localSaleId..."
$sales = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sales?storeId=$StoreId&terminalId=$terminalId&status=completed&limit=50" `
  -Headers $adminHeaders

$materializedSale = $sales | Where-Object { $_.localSaleId -eq $localSaleId } | Select-Object -First 1
if ($null -eq $materializedSale) {
    throw "Remote sale was not materialized for inventory-cache localSaleId=$localSaleId."
}

$saleDetail = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sales/$($materializedSale.id)" `
  -Headers $adminHeaders

if ($saleDetail.status -ne "completed") {
    throw "Expected materialized sale status completed but got $($saleDetail.status)."
}
if ($saleDetail.totalCents -ne $ExpectedPriceCents) {
    throw "Expected materialized sale total $ExpectedPriceCents but got $($saleDetail.totalCents)."
}

Write-Host "Reconciling local estimated inventory movements against remote inventory ledger..."
$remoteMovementCountSql = "select count(*) from pos.inventory_ledger where tenant_id = '$TenantId'::uuid and reference_type = 'sale' and reference_id = '$($materializedSale.id)'::uuid;"
$remoteMovementCount = [int](Invoke-RemoteScalarSql -Sql $remoteMovementCountSql)
if ($remoteMovementCount -lt 1) {
    throw "Expected remote inventory ledger movements for sale $($materializedSale.id), got 0."
}
Invoke-PosCoreCli -CliArgs @(
  "inventory-reconcile", "--db", $DatabasePath,
  "--local-sale-id", $localSaleId,
  "--remote-movement-count", $remoteMovementCount.ToString()
)

Write-Host "Issuing digital receipt for inventory-cache offline sale..."
$receiptBody = @{ customerEmail = $null } | ConvertTo-Json
$receipt = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/receipts/$($materializedSale.id)/issue" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $receiptBody

Write-Host "Validating pull/status/dead-letter after inventory-cache sale processing..."
$pull = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/pull?cursor=&limit=25" `
  -Headers $terminalHeaders

$status = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/status?storeId=$StoreId&terminalId=$terminalId" `
  -Headers $adminHeaders

$deadLetter = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/dead-letter?terminalId=$terminalId&limit=10" `
  -Headers $adminHeaders

Write-Host "Closing cash shift used by inventory-cache offline sale..."
$closeShiftBody = @{
  closedByUserId = $adminUserId
  countedCashCents = ($OpeningAmountCents + $ExpectedPriceCents)
} | ConvertTo-Json
$closedShift = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/close" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $closeShiftBody

$summary = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/summary" `
  -Headers $adminHeaders

Write-Host "Local outbox final status..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $terminalId
  databasePath = $DatabasePath
  sku = $Sku
  batchId = $batchId
  localSaleId = $localSaleId
  remoteSaleId = $materializedSale.id
  receiptId = $receipt.id
  receiptNumber = $receipt.receiptNumber
  shiftId = $shift.id
  shiftStatus = $closedShift.status
  processedCount = $process.processedCount
  pullChangeCount = $pull.changes.Count
  syncStatusProcessedCount = $status.processedCount
  syncStatusDeadLetterCount = $status.deadLetterCount
  deadLetterListCount = $deadLetter.Count
  remoteInventoryMovementCount = $remoteMovementCount
  saleTotalCents = $saleDetail.totalCents
  cashSalesCents = $summary.cashSalesCents
  expectedCashCents = $summary.expectedCashCents
  differenceCents = $summary.differenceCents
  message = "PosCore local inventory consumption cache completed."
}
