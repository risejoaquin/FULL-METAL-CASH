param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$StoreId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$DatabasePath = ".\.runtime\poscore-iteration-10.sqlite",
    [string]$Sku = "QSR-AMERICANO",
    [int]$ExpectedPriceCents = 4500,
    [int]$TenderedCents = 5000,
    [int]$OpeningAmountCents = 10000,
    [int]$CashInCents = 1000,
    [int]$CashOutCents = 500
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

$fingerprint = "iteration-10-poscore-offline-cash-$([guid]::NewGuid())"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 10 PosCore Offline Cash Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-10"
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
Invoke-PosCoreCli -CliArgs @("catalog-status", "--db", $DatabasePath, "--sku", $Sku)

Write-Host "Opening remote cash shift..."
$openShiftBody = @{
  storeId = $StoreId
  terminalId = $terminalId
  openedByUserId = $adminUserId
  openingAmountCents = $OpeningAmountCents
} | ConvertTo-Json

$remoteShift = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $openShiftBody

$localShiftId = [guid]::NewGuid().ToString()
Write-Host "Opening local cash shift..."
Invoke-PosCoreCli -CliArgs @(
  "open-local-shift", "--db", $DatabasePath,
  "--shift-id", $localShiftId,
  "--opened-by-user-id", $adminUserId,
  "--opening-amount-cents", $OpeningAmountCents.ToString()
)

Write-Host "Recording local and remote cash in/out..."
Invoke-PosCoreCli -CliArgs @("cash-in", "--db", $DatabasePath, "--shift-id", $localShiftId, "--amount-cents", $CashInCents.ToString(), "--note", "iteration-10-cash-in")
$remoteCashInBody = @{
  movementType = "cash_in"
  amountCents = $CashInCents
  reason = "iteration-10-cash-in"
  createdByUserId = $adminUserId
  authorizedByUserId = $adminUserId
} | ConvertTo-Json
$remoteCashIn = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts/$($remoteShift.id)/movements" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $remoteCashInBody

Invoke-PosCoreCli -CliArgs @("cash-out", "--db", $DatabasePath, "--shift-id", $localShiftId, "--amount-cents", $CashOutCents.ToString(), "--note", "iteration-10-cash-out")
$remoteCashOutBody = @{
  movementType = "cash_out"
  amountCents = $CashOutCents
  reason = "iteration-10-cash-out"
  createdByUserId = $adminUserId
  authorizedByUserId = $adminUserId
} | ConvertTo-Json
$remoteCashOut = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts/$($remoteShift.id)/movements" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $remoteCashOutBody

Write-Host "Local cash shift before sale..."
Invoke-PosCoreCli -CliArgs @("cash-status", "--db", $DatabasePath, "--shift-id", $localShiftId)

$batchId = [guid]::NewGuid().ToString()
$localSaleId = [guid]::NewGuid().ToString()
$expectedChangeCents = $TenderedCents - $ExpectedPriceCents

Write-Host "Creating offline cash sale from cached catalog SKU..."
Invoke-PosCoreCli -CliArgs @(
  "sale-offline-from-cache-cash", "--db", $DatabasePath,
  "--local-sale-id", $localSaleId,
  "--cashier-user-id", $adminUserId,
  "--sku", $Sku,
  "--quantity", "1",
  "--tendered-cents", $TenderedCents.ToString()
)

Write-Host "Local cash shift after sale..."
Invoke-PosCoreCli -CliArgs @("cash-status", "--db", $DatabasePath, "--shift-id", $localShiftId)

Write-Host "Local outbox before remote push..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

Write-Host "Pushing offline cash sale to PosServer..."
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $batchId,
  "--limit", "500"
)

Write-Host "Processing offline cash sale remote batch..."
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
    throw "Expected offline cash sale processing to process one event. Response: $serializedProcess"
}

Write-Host "Looking up materialized remote sale by localSaleId..."
$sales = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sales?storeId=$StoreId&terminalId=$terminalId&status=completed&limit=50" `
  -Headers $adminHeaders

$materializedSale = $sales | Where-Object { $_.localSaleId -eq $localSaleId } | Select-Object -First 1
if ($null -eq $materializedSale) {
    throw "Remote cash sale was not materialized for localSaleId=$localSaleId."
}

$saleDetail = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sales/$($materializedSale.id)" `
  -Headers $adminHeaders

if ($saleDetail.totalCents -ne $ExpectedPriceCents) {
    throw "Expected materialized sale total $ExpectedPriceCents but got $($saleDetail.totalCents)."
}

Write-Host "Issuing digital receipt for offline cash sale..."
$receiptBody = @{ customerEmail = $null } | ConvertTo-Json
$receipt = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/receipts/$($materializedSale.id)/issue" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $receiptBody

$expectedCashCents = $OpeningAmountCents + $CashInCents - $CashOutCents + $ExpectedPriceCents

Write-Host "Closing local cash shift..."
Invoke-PosCoreCli -CliArgs @(
  "close-local-shift", "--db", $DatabasePath,
  "--shift-id", $localShiftId,
  "--closed-by-user-id", $adminUserId,
  "--counted-cash-cents", $expectedCashCents.ToString()
)

Write-Host "Closing remote cash shift..."
$closeShiftBody = @{
  closedByUserId = $adminUserId
  countedCashCents = $expectedCashCents
} | ConvertTo-Json
$closedRemoteShift = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts/$($remoteShift.id)/close" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $closeShiftBody

$remoteSummary = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/cash-drawers/shifts/$($remoteShift.id)/summary" `
  -Headers $adminHeaders

Write-Host "Validating pull/status/dead-letter after offline cash sale processing..."
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

if ($remoteSummary.cashSalesCents -ne $ExpectedPriceCents) { throw "Remote cash sales mismatch. expected=$ExpectedPriceCents; actual=$($remoteSummary.cashSalesCents)." }
if ($remoteSummary.cashInCents -ne $CashInCents) { throw "Remote cash-in mismatch. expected=$CashInCents; actual=$($remoteSummary.cashInCents)." }
if ($remoteSummary.cashOutCents -ne $CashOutCents) { throw "Remote cash-out mismatch. expected=$CashOutCents; actual=$($remoteSummary.cashOutCents)." }
if ($remoteSummary.expectedCashCents -ne $expectedCashCents) { throw "Remote expected cash mismatch. expected=$expectedCashCents; actual=$($remoteSummary.expectedCashCents)." }
if ($remoteSummary.differenceCents -ne 0) { throw "Remote cash difference expected 0 but got $($remoteSummary.differenceCents)." }
if ($status.deadLetterCount -ne 0 -and $status.deadLetterCount -ne $null) { throw "Expected sync status deadLetterCount 0 but got $($status.deadLetterCount)." }
if ($deadLetter.Count -gt 0) { throw "Expected empty dead-letter list but got $($deadLetter.Count)." }

Write-Host "Local outbox final status..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)
Write-Host "Final local cash shift summary..."
Invoke-PosCoreCli -CliArgs @("cash-status", "--db", $DatabasePath, "--shift-id", $localShiftId)

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $terminalId
  databasePath = $DatabasePath
  localCashShiftId = $localShiftId
  remoteCashShiftId = $remoteShift.id
  batchId = $batchId
  localSaleId = $localSaleId
  remoteSaleId = $materializedSale.id
  receiptId = $receipt.id
  receiptNumber = $receipt.receiptNumber
  processedCount = $process.processedCount
  pullChangeCount = $pull.changes.Count
  syncStatusProcessedCount = $status.processedCount
  syncStatusDeadLetterCount = $status.deadLetterCount
  deadLetterListCount = $deadLetter.Count
  saleTotalCents = $saleDetail.totalCents
  tenderedCents = $TenderedCents
  changeCents = $expectedChangeCents
  cashSalesCents = $remoteSummary.cashSalesCents
  cashInCents = $remoteSummary.cashInCents
  cashOutCents = $remoteSummary.cashOutCents
  expectedCashCents = $remoteSummary.expectedCashCents
  countedCashCents = $remoteSummary.countedCashCents
  differenceCents = $remoteSummary.differenceCents
  message = "PosCore offline payment/cash drawer runtime completed."
}
