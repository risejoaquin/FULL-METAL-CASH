param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [string]$StoreCode = "MAIN",
    [string]$DatabasePath = ".\.runtime\pilot-06-sync-recovery-conflict-field-test.sqlite",
    [switch]$SkipDashboardValidation
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-06] $Message" }
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
function Invoke-DbExec {
    param([Parameter(Mandatory = $true)] [string]$Sql)
    $global:LASTEXITCODE = 0
    docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:16 psql "$DatabaseUrl" -v ON_ERROR_STOP=1 -c $Sql | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "DB exec command failed." }
    $global:LASTEXITCODE = 0
}
function Get-ResponseItems {
    param($Response)
    if ($null -eq $Response) { return @() }
    if ($Response -is [System.Array]) { return @($Response) }
    if ($null -ne $Response.items) { return @($Response.items) }
    if ($null -ne $Response.data) { return @($Response.data) }
    if ($null -ne $Response.conflicts) { return @($Response.conflicts) }
    if ($null -ne $Response.events) { return @($Response.events) }
    if ($null -ne $Response.results) { return @($Response.results) }
    return @($Response)
}
function Get-IntValue {
    param($Object, [string[]]$Names, [int]$Default = 0)
    if ($null -eq $Object) { return $Default }
    foreach ($name in $Names) {
        if ($null -ne $Object.$name) { return [int]$Object.$name }
    }
    return $Default
}
function Find-ByGuidProperty {
    param($Items, [string[]]$Names, [string]$Expected)
    foreach ($item in @($Items)) {
        foreach ($name in $Names) {
            if ($null -ne $item.$name -and ([string]$item.$name).ToLowerInvariant() -eq $Expected.ToLowerInvariant()) { return $item }
        }
    }
    return $null
}
function Format-SafeCliArgs {
    param([string[]]$CliArgs)
    $safe = @()
    $redactNext = $false
    foreach ($arg in $CliArgs) {
        if ($redactNext) {
            $safe += "<redacted>"
            $redactNext = $false
            continue
        }
        $safe += $arg
        if ($arg -in @("--terminal-token", "--terminal-access-token", "--access-token", "--password")) {
            $redactNext = $true
        }
    }
    return ($safe -join ' ')
}
function Invoke-PosCoreCli {
    param([Parameter(Mandatory = $true)] [string[]]$CliArgs)
    $global:LASTEXITCODE = 0
    & dotnet run --project "src/PosCore/SolidPOS.PosCore.Cli/SolidPOS.PosCore.Cli.csproj" -- @CliArgs
    if ($LASTEXITCODE -ne 0) { throw "PosCore CLI command failed: $(Format-SafeCliArgs -CliArgs $CliArgs)" }
    $global:LASTEXITCODE = 0
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-06-sync-recovery-conflict-check.sql"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-06-sync-recovery-conflict-field-test-log.md"
$runtimeDirectory = Split-Path -Parent $DatabasePath
if ([string]::IsNullOrWhiteSpace($runtimeDirectory)) { $runtimeDirectory = "." }

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-06 validation."
Assert-True (Test-Path $sqlPath) "PILOT-06 SQL validator is missing."
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

Write-Step "Sync recovery/conflict contract lookup..."
$contract = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/contract" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([int]$contract.currentSchemaVersion -eq 4) "Sync contract currentSchemaVersion must be 4."
Assert-True (@($contract.supportedStatuses) -contains "retry_pending") "Sync contract must support retry_pending."
Assert-True (@($contract.supportedStatuses) -contains "dead_letter") "Sync contract must support dead_letter."
Assert-True (@($contract.supportedStatuses) -contains "conflict") "Sync contract must support conflict."
Assert-True (@($contract.conflictResolutionStrategies) -contains "use_server") "Sync contract must support use_server resolution."
$storeId = Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' limit 1;"
Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) "Store not found for PILOT-06."
Write-Step "Sync recovery/conflict contract lookup PASS"

Write-Step "Terminal enrollment/register..."
$tokenBody = @{ storeId = $storeId; expiresInMinutes = 30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType "application/json" -Body $tokenBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) "Enrollment token was not returned."
$terminalFingerprint = "pilot-06-$([guid]::NewGuid())"
$registerBody = @{ enrollmentToken = $enrollment.enrollmentToken; name = "PILOT-06 Sync Recovery Terminal"; fingerprint = $terminalFingerprint; appVersion = "pilot-06" } | ConvertTo-Json
$terminalSession = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/terminal/register" -ContentType "application/json" -Body $registerBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.accessToken)) "Terminal register did not return accessToken."
$terminalId = [string]$terminalSession.terminal.id
$terminalAccessToken = [string]$terminalSession.accessToken
$terminalHeaders = @{ Authorization = "Bearer $terminalAccessToken" }
$terminalRuntime = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/terminal/session" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([string]$terminalRuntime.terminalId -eq $terminalId) "Terminal runtime context mismatch."
$bootstrap = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/bootstrap" -Headers $terminalHeaders -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($bootstrap.initialCursor)) "Sync bootstrap did not return initialCursor."
Write-Step "Terminal enrollment/register PASS"

Write-Step "PosCore local runtime integrity baseline..."
Invoke-PosCoreCli -CliArgs @("init", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("bind", "--db", $DatabasePath, "--tenant-id", $TenantId, "--store-id", $storeId, "--terminal-id", $terminalId, "--terminal-token", $terminalAccessToken, "--fingerprint", $terminalFingerprint, "--terminal-name", "PILOT-06 Sync Recovery Terminal")
Invoke-PosCoreCli -CliArgs @("verify-local-integrity", "--db", $DatabasePath)
Write-Step "PosCore local runtime integrity baseline PASS"

$recoveryInboxId = [guid]::NewGuid().ToString()
$recoveryEventId = [guid]::NewGuid().ToString()
$recoveryEntityId = [guid]::NewGuid().ToString()
$recoveryBatchId = [guid]::NewGuid().ToString()
$deadLetterInboxId = [guid]::NewGuid().ToString()
$deadLetterEventId = [guid]::NewGuid().ToString()
$deadLetterEntityId = [guid]::NewGuid().ToString()
$deadLetterBatchId = [guid]::NewGuid().ToString()
$conflictBatchId = [guid]::NewGuid().ToString()
$conflictEventId = [guid]::NewGuid().ToString()
$conflictEntityId = [guid]::NewGuid().ToString()

Write-Step "Seed controlled stuck-processing and dead-letter events..."
$seedSql = "insert into pos.sync_inbox_events (id, tenant_id, store_id, terminal_id, batch_id, event_id, event_type, entity_type, entity_id, local_occurred_at, schema_version, sequence_number, payload_hash, payload, status, attempts, max_attempts, last_attempt_at, next_retry_at, error_code, error_message, created_at) values ('$recoveryInboxId', '$TenantId', '$storeId', '$terminalId', '$recoveryBatchId', '$recoveryEventId', 'pos.health_check', 'sync_recovery_probe', '$recoveryEntityId', now() - interval '20 minutes', 4, 1, '$recoveryEventId', jsonb_build_object('source','pilot-06-stuck-processing'), 'processing', 1, 3, now() - interval '20 minutes', null, 'stuck_processing_seed', 'PILOT-06 controlled stuck processing event', now() - interval '20 minutes') on conflict (tenant_id, terminal_id, event_id) do nothing; insert into pos.sync_inbox_events (id, tenant_id, store_id, terminal_id, batch_id, event_id, event_type, entity_type, entity_id, local_occurred_at, schema_version, sequence_number, payload_hash, payload, status, attempts, max_attempts, last_attempt_at, dead_lettered_at, error_code, error_message, created_at) values ('$deadLetterInboxId', '$TenantId', '$storeId', '$terminalId', '$deadLetterBatchId', '$deadLetterEventId', 'pilot.unsupported_event', 'sync_dead_letter_probe', '$deadLetterEntityId', now() - interval '10 minutes', 4, 1, '$deadLetterEventId', jsonb_build_object('source','pilot-06-dead-letter'), 'dead_letter', 1, 1, now() - interval '10 minutes', now() - interval '9 minutes', 'unsupported_event_type', 'PILOT-06 controlled dead-letter event', now() - interval '10 minutes') on conflict (tenant_id, terminal_id, event_id) do nothing;"
Invoke-DbExec $seedSql
Write-Step "Seed controlled stuck-processing and dead-letter events PASS"

Write-Step "Stuck processing recovery validation..."
$recoveryBody = @{ batchId = $recoveryBatchId; maxEvents = 10 } | ConvertTo-Json
$recoveryProcess = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/process" -Headers $terminalHeaders -ContentType "application/json" -Body $recoveryBody -TimeoutSec 30
Assert-True ((Get-IntValue $recoveryProcess @("processedCount")) -ge 1) "Expected recovered event to be processed."
$recoveryStatus = Invoke-DbScalar "select status from pos.sync_inbox_events where tenant_id = '$TenantId' and id = '$recoveryInboxId';"
Assert-True ($recoveryStatus -eq "processed") "Recovered stuck-processing event did not end as processed."
Write-Step "Stuck processing recovery validation PASS"

Write-Step "Dead-letter list, retry, and re-dead-letter validation..."
$deadLetterBefore = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?terminalId=$terminalId&limit=50" -Headers $adminHeaders -TimeoutSec 30
$deadLetterMatch = Find-ByGuidProperty -Items (Get-ResponseItems $deadLetterBefore) -Names @("id", "inboxEventId") -Expected $deadLetterInboxId
Assert-True ($null -ne $deadLetterMatch) "Controlled dead-letter event was not returned by API."
$retryBody = @{ reason = "PILOT-06 controlled retry validation" } | ConvertTo-Json
$retry = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/dead-letter/$deadLetterInboxId/retry" -Headers $adminHeaders -ContentType "application/json" -Body $retryBody -TimeoutSec 30
Assert-True ([string]$retry.status -eq "retry_pending") "Dead-letter retry did not return retry_pending."
$retryStatus = Invoke-DbScalar "select status from pos.sync_inbox_events where tenant_id = '$TenantId' and id = '$deadLetterInboxId';"
Assert-True ($retryStatus -eq "retry_pending") "Dead-letter event was not scheduled as retry_pending."
$deadLetterProcessBody = @{ batchId = $deadLetterBatchId; maxEvents = 10 } | ConvertTo-Json
$deadLetterProcess = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/process" -Headers $terminalHeaders -ContentType "application/json" -Body $deadLetterProcessBody -TimeoutSec 30
Assert-True ((Get-IntValue $deadLetterProcess @("rejectedCount")) -ge 1) "Expected retried unsupported event to be rejected."
$deadLetterAfterStatus = Invoke-DbScalar "select status from pos.sync_inbox_events where tenant_id = '$TenantId' and id = '$deadLetterInboxId';"
Assert-True ($deadLetterAfterStatus -eq "dead_letter") "Retried unsupported event did not return to dead_letter."
Write-Step "Dead-letter list, retry, and re-dead-letter validation PASS"

Write-Step "Conflict generation through real sync push/process..."
$voidPayload = @{ saleId = $conflictEntityId; localSaleId = $null; voidedByUserId = $adminUserId; reason = "PILOT-06 controlled conflict - nonexistent sale"; occurredAt = (Get-Date).ToUniversalTime().ToString("o") }
$conflictPushBody = @{ batchId = $conflictBatchId; events = @(@{ eventId = $conflictEventId; eventType = "sale.voided"; entityType = "sale"; entityId = $conflictEntityId; localOccurredAt = (Get-Date).ToUniversalTime().ToString("o"); schemaVersion = 4; payload = $voidPayload }) } | ConvertTo-Json -Depth 30
$conflictPush = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/push" -Headers $terminalHeaders -ContentType "application/json" -Body $conflictPushBody -TimeoutSec 30
Assert-True ((Get-IntValue $conflictPush @("acceptedCount")) -eq 1) "Conflict probe sync push was not accepted."
$conflictProcessBody = @{ batchId = $conflictBatchId; maxEvents = 10 } | ConvertTo-Json
$conflictProcess = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/process" -Headers $terminalHeaders -ContentType "application/json" -Body $conflictProcessBody -TimeoutSec 30
$conflictResultItems = Get-ResponseItems $conflictProcess.results
$conflictProcessMatch = $conflictResultItems | Where-Object { ([string]$_.eventId).ToLowerInvariant() -eq $conflictEventId.ToLowerInvariant() -and [string]$_.status -eq "conflict" } | Select-Object -First 1
Assert-True ($null -ne $conflictProcessMatch) "Conflict probe did not produce conflict process result."
$conflicts = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=50" -Headers $adminHeaders -TimeoutSec 30
$conflictMatch = Find-ByGuidProperty -Items (Get-ResponseItems $conflicts) -Names @("localEventId", "local_event_id") -Expected $conflictEventId
Assert-True ($null -ne $conflictMatch) "Pending conflict was not returned by API."
$conflictId = [string]$conflictMatch.id
Write-Step "Conflict generation through real sync push/process PASS"

Write-Step "Conflict resolution through API..."
$resolveBody = @{ resolutionStrategy = "use_server"; resolvedPayload = $null; resolutionNote = "PILOT-06 controlled use_server resolution" } | ConvertTo-Json -Depth 30
$resolvedConflict = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sync/conflicts/$conflictId/resolve" -Headers $adminHeaders -ContentType "application/json" -Body $resolveBody -TimeoutSec 30
Assert-True ([string]$resolvedConflict.status -eq "resolved") "Conflict was not resolved."
Assert-True ([string]$resolvedConflict.resolutionStrategy -eq "use_server") "Conflict resolution strategy mismatch."
$pendingAfterResolve = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=50" -Headers $adminHeaders -TimeoutSec 30
$pendingSameConflict = Find-ByGuidProperty -Items (Get-ResponseItems $pendingAfterResolve) -Names @("id") -Expected $conflictId
Assert-True ($null -eq $pendingSameConflict) "Resolved conflict still appears in pending list."
Write-Step "Conflict resolution through API PASS"

Write-Step "Sync status, SQL validation and local recovery journal..."
$status = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status?storeId=$storeId&terminalId=$terminalId" -Headers $adminHeaders -TimeoutSec 30
Invoke-PosCoreCli -CliArgs @("verify-local-integrity", "--db", $DatabasePath)
Invoke-PosCoreCli -CliArgs @("recovery-journal", "--db", $DatabasePath)
$sqlOutput = Invoke-DbFile -SqlPath $sqlPath -Variables @{
    tenant_id = $TenantId
    store_id = $storeId
    terminal_id = $terminalId
    recovery_inbox_id = $recoveryInboxId
    dead_letter_inbox_id = $deadLetterInboxId
    conflict_id = $conflictId
    conflict_event_id = $conflictEventId
}
$sqlText = ($sqlOutput | Out-String)
Assert-True ($sqlText -match "GO") "PILOT-06 SQL validation did not return GO."
Write-Step "Sync status, SQL validation and local recovery journal PASS"

$result = [pscustomobject]@{
    tenantId = $TenantId
    storeId = $storeId
    terminalId = $terminalId
    localDatabasePath = $DatabasePath
    recoveryInboxId = $recoveryInboxId
    recoveryEventId = $recoveryEventId
    recoveryBatchId = $recoveryBatchId
    recoveryStatus = $recoveryStatus
    deadLetterInboxId = $deadLetterInboxId
    deadLetterEventId = $deadLetterEventId
    deadLetterBatchId = $deadLetterBatchId
    retryStatus = [string]$retry.status
    deadLetterAfterStatus = $deadLetterAfterStatus
    conflictId = $conflictId
    conflictEventId = $conflictEventId
    conflictBatchId = $conflictBatchId
    conflictResolutionStrategy = [string]$resolvedConflict.resolutionStrategy
    conflictStatus = [string]$resolvedConflict.status
    processedCount = (Get-IntValue $recoveryProcess @("processedCount"))
    deadLetterRejectedCount = (Get-IntValue $deadLetterProcess @("rejectedCount"))
    conflictCount = (Get-IntValue $status @("conflictCount"))
    deadLetterCount = (Get-IntValue $status @("deadLetterCount"))
    schemaVersion = [int]$contract.currentSchemaVersion
    bootstrapInitialCursor = [string]$bootstrap.initialCursor
    syncContract = "schema_version_4"
    goNoGo = "GO"
    message = "SolidPOS PILOT-06 sync recovery conflict field test completed."
}

Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS PILOT-06 Sync Recovery Conflict Field Test Log"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "Status: PASS REAL PRODUCTION / GO"
Add-Content -Path $logPath -Encoding UTF8 -Value "TenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "StoreId: $storeId"
Add-Content -Path $logPath -Encoding UTF8 -Value "TerminalId: $terminalId"
Add-Content -Path $logPath -Encoding UTF8 -Value "LocalDatabasePath: $DatabasePath"
Add-Content -Path $logPath -Encoding UTF8 -Value "RecoveryInboxId: $recoveryInboxId"
Add-Content -Path $logPath -Encoding UTF8 -Value "RecoveryEventId: $recoveryEventId"
Add-Content -Path $logPath -Encoding UTF8 -Value "RecoveryBatchId: $recoveryBatchId"
Add-Content -Path $logPath -Encoding UTF8 -Value "RecoveryStatus: $recoveryStatus"
Add-Content -Path $logPath -Encoding UTF8 -Value "DeadLetterInboxId: $deadLetterInboxId"
Add-Content -Path $logPath -Encoding UTF8 -Value "DeadLetterEventId: $deadLetterEventId"
Add-Content -Path $logPath -Encoding UTF8 -Value "DeadLetterBatchId: $deadLetterBatchId"
Add-Content -Path $logPath -Encoding UTF8 -Value "RetryStatus: $($retry.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "DeadLetterAfterStatus: $deadLetterAfterStatus"
Add-Content -Path $logPath -Encoding UTF8 -Value "ConflictId: $conflictId"
Add-Content -Path $logPath -Encoding UTF8 -Value "ConflictEventId: $conflictEventId"
Add-Content -Path $logPath -Encoding UTF8 -Value "ConflictBatchId: $conflictBatchId"
Add-Content -Path $logPath -Encoding UTF8 -Value "ConflictResolutionStrategy: $($resolvedConflict.resolutionStrategy)"
Add-Content -Path $logPath -Encoding UTF8 -Value "ConflictStatus: $($resolvedConflict.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "SchemaVersion: $($contract.currentSchemaVersion)"
Add-Content -Path $logPath -Encoding UTF8 -Value "BootstrapInitialCursor: $($bootstrap.initialCursor)"
Add-Content -Path $logPath -Encoding UTF8 -Value "SyncContract: schema_version_4"
Add-Content -Path $logPath -Encoding UTF8 -Value "GoNoGo: GO"
Add-Content -Path $logPath -Encoding UTF8 -Value ""
Add-Content -Path $logPath -Encoding UTF8 -Value "SQL validation output:"
Add-Content -Path $logPath -Encoding UTF8 -Value $sqlText

Write-Step "PILOT-06 PASS REAL PRODUCTION / GO"
$result
