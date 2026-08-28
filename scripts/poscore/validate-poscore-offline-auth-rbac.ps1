param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$StoreId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$DatabasePath = ".\.runtime\poscore-iteration-12.sqlite",
    [string]$Sku = "QSR-AMERICANO",
    [int]$ExpectedPriceCents = 4500,
    [int]$TenderedCents = 5000,
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
    if ($LASTEXITCODE -ne 0) { throw "PosCore CLI command failed: $($CliArgs -join ' ')" }
}

function Invoke-PosCoreCliOutput {
    param([Parameter(Mandatory = $true)] [string[]]$CliArgs)
    $output = dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- @CliArgs 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "PosCore CLI command failed: $($CliArgs -join ' ')" }
    return ($output -join "`n")
}

Write-Host "Logging in as production admin..."
$loginBody = @{ email = $AdminEmail; password = $AdminPassword; tenantId = $TenantId } | ConvertTo-Json
$adminSession = Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody
$adminUserId = $adminSession.user.id
if ([string]::IsNullOrWhiteSpace($adminUserId)) { throw "Admin auth response did not include user.id." }
$adminHeaders = @{ Authorization = "Bearer $($adminSession.accessToken)" }

Write-Host "Creating terminal enrollment token..."
$tokenBody = @{ storeId = $StoreId; expiresInMinutes = 30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType "application/json" -Body $tokenBody

$fingerprint = "iteration-12-poscore-offline-auth-$([guid]::NewGuid())"
$terminalBody = @{ enrollmentToken = $enrollment.enrollmentToken; name = "Iteration 12 PosCore Offline Auth Terminal"; fingerprint = $fingerprint; appVersion = "iteration-12" } | ConvertTo-Json
Write-Host "Registering remote terminal session..."
$terminalSession = Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/terminal/register" -ContentType "application/json" -Body $terminalBody
$terminalId = $terminalSession.terminal.id
$terminalAccessToken = $terminalSession.accessToken
$terminalHeaders = @{ Authorization = "Bearer $terminalAccessToken" }

Write-Host "Initializing local PosCore SQLite runtime..."
Invoke-PosCoreCli -CliArgs @("init", "--db", $DatabasePath)

Write-Host "Binding local terminal to remote terminal session..."
Invoke-PosCoreCli -CliArgs @("bind", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $StoreId, "--terminal-id", $terminalId, "--fingerprint", $fingerprint, "--terminal-token", $terminalAccessToken, "--schema-version", "4")

Write-Host "Caching production admin user/permissions locally..."
$permissions = "sales.create,sales.read,sync.push,sync.pull,cash.shift.open,cash.shift.close,receipts.issue"
Invoke-PosCoreCli -CliArgs @("sync-local-user", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $StoreId, "--user-id", $adminUserId, "--email", $AdminEmail, "--display-name", "Production Admin", "--password", $AdminPassword, "--role", "owner", "--permissions", $permissions, "--max-offline-hours", "72")
Invoke-PosCoreCli -CliArgs @("auth-status", "--db", $DatabasePath)

Write-Host "Validating local login/session/RBAC..."
$loginOutput = Invoke-PosCoreCliOutput -CliArgs @("login-local", "--db", $DatabasePath, "--email", $AdminEmail, "--password", $AdminPassword)
$sessionId = [regex]::Match($loginOutput, "sessionId=([0-9a-fA-F-]{36})").Groups[1].Value
if ([string]::IsNullOrWhiteSpace($sessionId)) { throw "Could not parse local session id." }
Invoke-PosCoreCli -CliArgs @("whoami-local", "--db", $DatabasePath, "--session-id", $sessionId)
Invoke-PosCoreCli -CliArgs @("require-permission-local", "--db", $DatabasePath, "--session-id", $sessionId, "--permission", "sales.create")
Invoke-PosCoreCli -CliArgs @("require-permission-local", "--db", $DatabasePath, "--session-id", $sessionId, "--permission", "sync.push")

Write-Host "Validating 72-hour offline window block with expired cached user..."
$expiredUserId = [guid]::NewGuid().ToString()
Invoke-PosCoreCli -CliArgs @("sync-local-user", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $StoreId, "--user-id", $expiredUserId, "--email", "expired.iteration12@solidpos.local", "--display-name", "Expired Offline User", "--password", "ExpiredUser123!", "--role", "cashier", "--permissions", "sales.create", "--max-offline-hours", "72", "--last-sync-hours-ago", "73")
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $expiredOutput = dotnet run --project src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj -- login-local --db $DatabasePath --email "expired.iteration12@solidpos.local" --password "ExpiredUser123!" 2>&1
    $expiredExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
$expiredOutput | ForEach-Object { Write-Host $_ }
if ($expiredExitCode -eq 0) { throw "Expired offline user login unexpectedly succeeded." }
if (($expiredOutput -join "`n") -notmatch "offline window") { throw "Expired user failure did not mention offline window." }
Write-Host "Expired offline user blocked correctly."

Write-Host "Syncing remote catalog into local SQLite cache..."
Invoke-PosCoreCli -CliArgs @("sync-catalog", "--db", $DatabasePath, "--base-url", $base, "--access-token", $terminalAccessToken)
Invoke-PosCoreCli -CliArgs @("catalog-status", "--db", $DatabasePath, "--sku", $Sku)

Write-Host "Opening remote/local cash shift after local RBAC approval..."
$openShiftBody = @{ storeId = $StoreId; terminalId = $terminalId; openedByUserId = $adminUserId; openingAmountCents = $OpeningAmountCents } | ConvertTo-Json
$remoteShift = Invoke-RestMethod -Method Post -Uri "$base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType "application/json" -Body $openShiftBody
$localShiftId = [guid]::NewGuid().ToString()
Invoke-PosCoreCli -CliArgs @("open-local-shift", "--db", $DatabasePath, "--shift-id", $localShiftId, "--opened-by-user-id", $adminUserId, "--opening-amount-cents", $OpeningAmountCents.ToString())

$batchId = [guid]::NewGuid().ToString()
$localSaleId = [guid]::NewGuid().ToString()
$expectedChangeCents = $TenderedCents - $ExpectedPriceCents
Write-Host "Creating offline sale under local authenticated session..."
Invoke-PosCoreCli -CliArgs @("sale-offline-from-cache-cash", "--db", $DatabasePath, "--local-sale-id", $localSaleId, "--cashier-user-id", $adminUserId, "--sku", $Sku, "--quantity", "1", "--tendered-cents", $TenderedCents.ToString())

Write-Host "Pushing authenticated offline sale to PosServer..."
Invoke-PosCoreCli -CliArgs @("sync-push", "--db", $DatabasePath, "--base-url", $base, "--batch-id", $batchId, "--limit", "500")
$processBody = @{ batchId = $batchId; maxEvents = 10 } | ConvertTo-Json
$process = Invoke-RestMethod -Method Post -Uri "$base/api/v1/sync/process" -Headers $terminalHeaders -ContentType "application/json" -Body $processBody
if ($process.processedCount -lt 1) { throw "Expected one processed event, got $($process.processedCount)." }

Write-Host "Looking up materialized remote sale and issuing receipt..."
$sales = Invoke-RestMethod -Method Get -Uri "$base/api/v1/sales?storeId=$StoreId&terminalId=$terminalId&status=completed&limit=50" -Headers $adminHeaders
$materializedSale = $sales | Where-Object { $_.localSaleId -eq $localSaleId } | Select-Object -First 1
if ($null -eq $materializedSale) { throw "Remote sale was not materialized for localSaleId=$localSaleId." }
$saleDetail = Invoke-RestMethod -Method Get -Uri "$base/api/v1/sales/$($materializedSale.id)" -Headers $adminHeaders
$receipt = Invoke-RestMethod -Method Post -Uri "$base/api/v1/receipts/$($materializedSale.id)/issue" -Headers $terminalHeaders -ContentType "application/json" -Body (@{ customerEmail = $null } | ConvertTo-Json)

Write-Host "Closing local/remote cash shift and validating diagnostics..."
$expectedCashCents = $OpeningAmountCents + $ExpectedPriceCents
Invoke-PosCoreCli -CliArgs @("close-local-shift", "--db", $DatabasePath, "--shift-id", $localShiftId, "--closed-by-user-id", $adminUserId, "--counted-cash-cents", $expectedCashCents.ToString())
$closedRemoteShift = Invoke-RestMethod -Method Post -Uri "$base/api/v1/cash-drawers/shifts/$($remoteShift.id)/close" -Headers $terminalHeaders -ContentType "application/json" -Body (@{ closedByUserId = $adminUserId; countedCashCents = $expectedCashCents } | ConvertTo-Json)
$remoteSummary = Invoke-RestMethod -Method Get -Uri "$base/api/v1/cash-drawers/shifts/$($remoteShift.id)/summary" -Headers $adminHeaders
$status = Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/status?storeId=$StoreId&terminalId=$terminalId" -Headers $adminHeaders
$deadLetter = Invoke-RestMethod -Method Get -Uri "$base/api/v1/sync/dead-letter?terminalId=$terminalId&limit=10" -Headers $adminHeaders
if ($remoteSummary.differenceCents -ne 0) { throw "Remote cash difference expected 0 but got $($remoteSummary.differenceCents)." }
if ($deadLetter.Count -gt 0) { throw "Expected empty dead-letter list but got $($deadLetter.Count)." }

Write-Host "Local auth final status and logout..."
Invoke-PosCoreCli -CliArgs @("auth-status", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("logout-local", "--db", $DatabasePath, "--session-id", $sessionId)
Invoke-PosCoreCli -CliArgs @("auth-status", "--db", $DatabasePath)
Write-Host "Local outbox final status..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $terminalId
  databasePath = $DatabasePath
  sessionId = $sessionId
  localCashShiftId = $localShiftId
  remoteCashShiftId = $remoteShift.id
  batchId = $batchId
  localSaleId = $localSaleId
  remoteSaleId = $materializedSale.id
  receiptId = $receipt.id
  receiptNumber = $receipt.receiptNumber
  processedCount = $process.processedCount
  syncStatusProcessedCount = $status.processedCount
  syncStatusDeadLetterCount = $status.deadLetterCount
  deadLetterListCount = $deadLetter.Count
  saleTotalCents = $saleDetail.totalCents
  tenderedCents = $TenderedCents
  changeCents = $expectedChangeCents
  expectedCashCents = $remoteSummary.expectedCashCents
  countedCashCents = $remoteSummary.countedCashCents
  differenceCents = $remoteSummary.differenceCents
  message = "PosCore offline auth/session/RBAC runtime completed."
}
