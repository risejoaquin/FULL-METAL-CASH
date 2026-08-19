param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$StoreId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$DatabasePath = ".\.runtime\poscore-iteration-08.sqlite",
    [string]$Sku = "QSR-AMERICANO",
    [int]$ExpectedPriceCents = 4500,
    [int]$OpeningAmountCents = 10000
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
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

$fingerprint = "iteration-08-poscore-catalog-cache-$([guid]::NewGuid())"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 08 PosCore Catalog Cache Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-08"
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

Write-Host "Syncing remote catalog into local SQLite cache..."
Invoke-PosCoreCli -CliArgs @(
  "sync-catalog", "--db", $DatabasePath,
  "--base-url", $base,
  "--access-token", $terminalAccessToken
)

Write-Host "Checking cached product by SKU..."
Invoke-PosCoreCli -CliArgs @("catalog-status", "--db", $DatabasePath, "--sku", $Sku)

Write-Host "Opening remote cash shift for cached catalog offline sale..."
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

Write-Host "Creating offline sale from cached catalog SKU..."
Invoke-PosCoreCli -CliArgs @(
  "sale-offline-from-cache", "--db", $DatabasePath,
  "--local-sale-id", $localSaleId,
  "--cashier-user-id", $adminUserId,
  "--sku", $Sku,
  "--quantity", "1"
)

Write-Host "Local outbox before remote push..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

Write-Host "Pushing cached-catalog sale.completed event to PosServer..."
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $batchId,
  "--limit", "500"
)

Write-Host "Processing cached-catalog sale.completed remote batch..."
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
    throw "Expected cached catalog sale.completed processing to process one event. Response: $serializedProcess"
}

Write-Host "Looking up materialized remote sale by localSaleId..."
$sales = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sales?storeId=$StoreId&terminalId=$terminalId&status=completed&limit=50" `
  -Headers $adminHeaders

$materializedSale = $sales | Where-Object { $_.localSaleId -eq $localSaleId } | Select-Object -First 1
if ($null -eq $materializedSale) {
    throw "Remote sale was not materialized for cached catalog localSaleId=$localSaleId."
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
$saleLines = @($saleDetail.lines)
if ($saleLines.Count -lt 1) {
    throw "Expected materialized sale to include at least one line."
}
if ($saleLines[0].description -ne "Americano 12oz") {
    throw "Expected cached product snapshot description Americano 12oz but got $($saleLines[0].description)."
}
if ($saleLines[0].unitPriceCents -ne $ExpectedPriceCents) {
    throw "Expected cached product snapshot price $ExpectedPriceCents but got $($saleLines[0].unitPriceCents)."
}

Write-Host "Issuing digital receipt for cached-catalog offline sale..."
$receiptBody = @{ customerEmail = $null } | ConvertTo-Json
$receipt = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/receipts/$($materializedSale.id)/issue" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $receiptBody

Write-Host "Validating pull/status/dead-letter after cached catalog sale processing..."
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

Write-Host "Closing cash shift used by cached catalog offline sale..."
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
  saleDescription = $saleLines[0].description
  saleUnitPriceCents = $saleLines[0].unitPriceCents
  saleTotalCents = $saleDetail.totalCents
  cashSalesCents = $summary.cashSalesCents
  expectedCashCents = $summary.expectedCashCents
  differenceCents = $summary.differenceCents
  message = "PosCore local catalog cache offline sale completed."
}
