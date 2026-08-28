param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$StoreId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$DatabasePath = ".\.runtime\poscore-iteration-06.sqlite"
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

$fingerprint = "iteration-06-poscore-sync-runtime-$TenantId"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 06 PosCore Sync Runtime Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-06"
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

$firstBatchId = [guid]::NewGuid().ToString()
Write-Host "Queueing processable local health-check outbox event..."
Invoke-PosCoreCli -CliArgs @("queue-health-check", "--db", $DatabasePath, "--source", "iteration-06-processed-validation")
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

Write-Host "Pushing processable local event to PosServer..."
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $firstBatchId,
  "--limit", "500"
)

Write-Host "Processing first remote sync batch..."
$processBody = @{
  batchId = $firstBatchId
  maxEvents = 10
} | ConvertTo-Json
$firstProcess = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/sync/process" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $processBody

if ($firstProcess.processedCount -lt 1) {
    throw "Expected first remote processing to process at least one event. processedCount=$($firstProcess.processedCount)"
}

Write-Host "Requeueing already-synced local event to validate remote duplicate handling..."
Invoke-PosCoreCli -CliArgs @("requeue-latest-synced", "--db", $DatabasePath)
$duplicateBatchId = [guid]::NewGuid().ToString()
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $duplicateBatchId,
  "--limit", "500"
)

Write-Host "Queueing and forcing local failed event for retry validation..."
Invoke-PosCoreCli -CliArgs @("queue-health-check", "--db", $DatabasePath, "--source", "iteration-06-retry-validation")
Invoke-PosCoreCli -CliArgs @("fail-first-pending", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("retry-failed", "--db", $DatabasePath, "--max-attempts", "5")

$retryBatchId = [guid]::NewGuid().ToString()
Write-Host "Pushing retried local outbox event to PosServer..."
Invoke-PosCoreCli -CliArgs @(
  "sync-push", "--db", $DatabasePath,
  "--base-url", $base,
  "--batch-id", $retryBatchId,
  "--limit", "500"
)

Write-Host "Processing retried remote sync batch..."
$retryProcessBody = @{
  batchId = $retryBatchId
  maxEvents = 10
} | ConvertTo-Json
$retryProcess = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/sync/process" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $retryProcessBody

if ($retryProcess.processedCount -lt 1) {
    throw "Expected retried remote processing to process at least one event. processedCount=$($retryProcess.processedCount)"
}

Write-Host "Validating pull/status/dead-letter diagnostics..."
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

Write-Host "Local outbox final status..."
Invoke-PosCoreCli -CliArgs @("outbox-status", "--db", $DatabasePath)

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $StoreId
  terminalId = $terminalId
  databasePath = $DatabasePath
  firstBatchId = $firstBatchId
  duplicateBatchId = $duplicateBatchId
  retryBatchId = $retryBatchId
  firstProcessedCount = $firstProcess.processedCount
  retryProcessedCount = $retryProcess.processedCount
  pullChangeCount = $pull.changes.Count
  syncStatusProcessedCount = $status.processedCount
  syncStatusDeadLetterCount = $status.deadLetterCount
  deadLetterListCount = $deadLetter.Count
  message = "PosCore sync processing/retry runtime completed."
}
