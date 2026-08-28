param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$StoreId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [Parameter(Mandatory = $true)] [string]$ProductId,
    [string]$DatabasePath = ".\.runtime\poscore-iteration-05.sqlite",
    [string]$Sku = "QSR-AMERICANO",
    [string]$ProductName = "Americano 12oz",
    [int]$PriceCents = 4500
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path (Split-Path $DatabasePath -Parent) | Out-Null

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

$fingerprint = "iteration-05-poscore-sync-$TenantId"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 05 PosCore Sync Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-05"
} | ConvertTo-Json

Write-Host "Registering remote terminal session..."
$terminalSession = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/auth/terminal/register" `
  -ContentType "application/json" `
  -Body $terminalBody

$terminalId = $terminalSession.terminal.id
$terminalAccessToken = $terminalSession.accessToken
$batchId = [guid]::NewGuid().ToString()

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

Write-Host "Creating offline sale locally..."
Invoke-PosCoreCli -CliArgs @(
  "sale-offline", "--db", $DatabasePath,
  "--product-id", $ProductId,
  "--sku", $Sku,
  "--name", $ProductName,
  "--price-cents", "$PriceCents",
  "--quantity", "1",
  "--currency", "MXN"
)

Write-Host "Local outbox before remote push..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

Write-Host "Pushing local outbox to PosServer..."
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $batchId,
  "--limit", "500"
)

Write-Host "Processing remote sync batch..."
$terminalHeaders = @{ Authorization = "Bearer $terminalAccessToken" }
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

Write-Host "Local outbox after remote push..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

$status = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/status?storeId=$StoreId&terminalId=$terminalId" `
  -Headers $adminHeaders

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $terminalId
  batchId = $batchId
  databasePath = $DatabasePath
  remoteProcessedCount = $process.processedCount
  remoteDuplicateCount = $process.duplicateCount
  remoteDeadLetterCount = $status.deadLetterCount
  message = "PosCore offline-to-online sync completed."
}
