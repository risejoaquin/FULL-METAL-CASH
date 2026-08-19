param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$StoreCode = "MAIN",
    [string]$DatabaseUrl = $env:DATABASE_URL
)

$ErrorActionPreference = "Stop"

function Invoke-DbScalar {
    param([Parameter(Mandatory = $true)] [string]$Sql)

    if ([string]::IsNullOrWhiteSpace($DatabaseUrl)) {
        throw "DATABASE_URL is required for sync E2E lookup."
    }

    $result = docker run --rm `
      --env "DATABASE_URL=$DatabaseUrl" `
      postgres:16 `
      psql "$DatabaseUrl" -tAc $Sql

    if ($LASTEXITCODE -ne 0) {
        throw "DB scalar command failed: $Sql"
    }

    return ($result | Select-Object -First 1).Trim()
}

$base = $BaseUrl.TrimEnd('/')
$storeId = Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' limit 1;"

if ([string]::IsNullOrWhiteSpace($storeId)) {
    throw "Active store $StoreCode was not found for tenant $TenantId."
}

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

$tokenBody = @{
  storeId = $storeId
  expiresInMinutes = 30
} | ConvertTo-Json

$enrollment = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/terminals/enrollment-token" `
  -Headers $adminHeaders `
  -ContentType "application/json" `
  -Body $tokenBody

$fingerprint = "iteration-03-sync-e2e-$TenantId"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 03 Sync E2E Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-03"
} | ConvertTo-Json

$terminalSession = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/auth/terminal/register" `
  -ContentType "application/json" `
  -Body $terminalBody

$terminalHeaders = @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$contract = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/contract" `
  -Headers $terminalHeaders

$batchId = [guid]::NewGuid().ToString()
$eventId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")

$pushBody = @{
  batchId = $batchId
  events = @(
    @{
      eventId = $eventId
      eventType = "pos.health_check"
      entityType = "terminal"
      entityId = $terminalSession.terminal.id
      localOccurredAt = $now
      schemaVersion = $contract.currentSchemaVersion
      payload = @{
        source = "iteration-03-sync-e2e"
        terminalId = $terminalSession.terminal.id
      }
    }
  )
} | ConvertTo-Json -Depth 8

$push = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/sync/push" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $pushBody

$duplicatePush = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/sync/push" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $pushBody

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

$pull = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/pull?cursor=&limit=25" `
  -Headers $terminalHeaders

$status = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/status?storeId=$storeId&terminalId=$($terminalSession.terminal.id)" `
  -Headers $adminHeaders

$deadLetter = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/sync/dead-letter?terminalId=$($terminalSession.terminal.id)&limit=10" `
  -Headers $adminHeaders

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $storeId
  terminalId = $terminalSession.terminal.id
  batchId = $batchId
  eventId = $eventId
  acceptedCount = $push.acceptedCount
  duplicateCount = $duplicatePush.duplicateCount
  processedCount = $process.processedCount
  pullChangeCount = $pull.changes.Count
  syncStatusProcessedCount = $status.processedCount
  syncStatusDeadLetterCount = $status.deadLetterCount
  deadLetterListCount = $deadLetter.Count
  message = "Offline sync E2E server contract completed."
}
