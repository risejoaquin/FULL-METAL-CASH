param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$KeepRestoreContainer
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-08] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString {
    param([securestring]$SecureValue)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Invoke-DbJsonFile {
    param(
        [Parameter(Mandatory = $true)] [string]$SqlPath,
        [Parameter(Mandatory = $true)] [hashtable]$Variables
    )
    $mountDirectory = (Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $fileName = Split-Path -Leaf $SqlPath
    $args = @("run", "--rm", "--env", "DATABASE_URL=$DatabaseUrl", "-v", "${mountDirectory}:/sql:ro", "postgres:17", "psql", "$DatabaseUrl", "-tA", "-v", "ON_ERROR_STOP=1")
    foreach ($key in $Variables.Keys) { $args += @("-v", "$key=$($Variables[$key])") }
    $args += @("-f", "/sql/$fileName")
    $global:LASTEXITCODE = 0
    $output = docker @args
    if ($LASTEXITCODE -ne 0) { throw "DB JSON file command failed for $SqlPath." }
    $global:LASTEXITCODE = 0
    $json = ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1)
    Assert-True (-not [string]::IsNullOrWhiteSpace($json)) "DB JSON file did not return JSON."
    return ($json | ConvertFrom-Json)
}
function Invoke-DockerCommand {
    param([Parameter(Mandatory = $true)] [string[]]$Arguments, [string]$FailureMessage)
    $global:LASTEXITCODE = 0
    $output = docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
    $global:LASTEXITCODE = 0
    return $output
}
function Invoke-DockerCommandNoOutput {
    param([Parameter(Mandatory = $true)] [string[]]$Arguments, [string]$FailureMessage)
    $global:LASTEXITCODE = 0
    docker @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
    $global:LASTEXITCODE = 0
}
function Wait-PostgresContainerReady {
    param([string]$ContainerName)
    for ($i = 0; $i -lt 60; $i++) {
        $global:LASTEXITCODE = 0
        docker exec -e PGPASSWORD=solidpos $ContainerName pg_isready -h 127.0.0.1 -p 5432 -U postgres -d solidpos_restore | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $global:LASTEXITCODE = 0
            docker exec -e PGPASSWORD=solidpos $ContainerName psql -h 127.0.0.1 -p 5432 -U postgres -d solidpos_restore -tA -v ON_ERROR_STOP=1 -c "SELECT 1;" | Out-Null
            if ($LASTEXITCODE -eq 0) { $global:LASTEXITCODE = 0; return }
        }
        Start-Sleep -Seconds 1
    }
    throw "Restore PostgreSQL container did not become ready."
}
function Invoke-RestorePsql {
    param(
        [Parameter(Mandatory = $true)] [string]$ContainerName,
        [Parameter(Mandatory = $true)] [string[]]$PsqlArguments,
        [Parameter(Mandatory = $true)] [string]$FailureMessage
    )
    $args = @("exec", "-e", "PGPASSWORD=solidpos", $ContainerName, "psql", "-h", "127.0.0.1", "-p", "5432", "-U", "postgres", "-d", "solidpos_restore") + $PsqlArguments
    Invoke-DockerCommandNoOutput -Arguments $args -FailureMessage $FailureMessage
}
function Invoke-RestorePsqlOutput {
    param(
        [Parameter(Mandatory = $true)] [string]$ContainerName,
        [Parameter(Mandatory = $true)] [string[]]$PsqlArguments,
        [Parameter(Mandatory = $true)] [string]$FailureMessage
    )
    $args = @("exec", "-e", "PGPASSWORD=solidpos", $ContainerName, "psql", "-h", "127.0.0.1", "-p", "5432", "-U", "postgres", "-d", "solidpos_restore") + $PsqlArguments
    return Invoke-DockerCommand -Arguments $args -FailureMessage $FailureMessage
}
function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlCheckPath = Join-Path $scriptRoot "pilot-08-backup-restore-check.sql"
$sqlRollbackPath = Join-Path $scriptRoot "pilot-08-rollback-transaction-check.sql"
$runtimeDirectory = Join-Path $repoRoot ".runtime\pilot-08-backup-restore-rollback-drill"
$backupDirectory = Join-Path $runtimeDirectory "backups"
$restoreDirectory = Join-Path $runtimeDirectory "restore"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-08-backup-restore-rollback-drill-log.md"
$backupPath = Join-Path $backupDirectory "pos-schema-backup.sql"
$manifestPath = Join-Path $backupDirectory "pilot-08-backup-manifest.json"
$restoreContainer = "solidpos-pilot-08-restore-$(([guid]::NewGuid().ToString('N')).Substring(0,12))"
$rollbackTraceId = "pilot-08-rollback-$(([guid]::NewGuid().ToString('N')).Substring(0,12))"
$rollbackEntityId = [guid]::NewGuid().ToString()
$containerStarted = $false

try {
    Write-Step "Local repository guardrails..."
    Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-08 validation."
    Assert-True (Test-Path $sqlCheckPath) "PILOT-08 backup/restore SQL validator is missing."
    Assert-True (Test-Path $sqlRollbackPath) "PILOT-08 rollback SQL validator is missing."
    Assert-True ($DatabaseUrl.StartsWith("postgresql://") -or $DatabaseUrl.StartsWith("postgres://")) "DATABASE_URL must be PostgreSQL/Supabase URL."
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $restoreDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    Write-Step "Local repository guardrails PASS"

    Write-Step "Local secret scan..."
    $global:LASTEXITCODE = 0
    & (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "Local secret scan failed." }
    $global:LASTEXITCODE = 0
    Write-Step "Local secret scan PASS"

    Write-Step "Docker and PostgreSQL tooling availability..."
    Invoke-DockerCommandNoOutput -Arguments @("version") -FailureMessage "Docker is required for PILOT-08 backup/restore drill."
    Invoke-DockerCommandNoOutput -Arguments @("run", "--rm", "postgres:17", "pg_dump", "--version") -FailureMessage "postgres:17 pg_dump is required for PILOT-08."
    Invoke-DockerCommandNoOutput -Arguments @("run", "--rm", "postgres:17", "psql", "--version") -FailureMessage "postgres:17 psql is required for PILOT-08."
    Write-Step "Docker and PostgreSQL tooling availability PASS"

    Write-Step "Production liveness/readiness..."
    $live = Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
    Assert-True ($live.status -eq "alive") "Production liveness did not return alive."
    $ready = Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
    Assert-True ($ready.status -eq "ready") "Production readiness did not return ready."
    Assert-True ($ready.database -eq "ready") "Production database readiness did not return ready."
    Write-Step "Production liveness/readiness PASS"

    Write-Step "Admin login and observability baseline..."
    $loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
    $session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
    Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
    $adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
    $metricsBefore = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
    Assert-True ($metricsBefore.database.ready -eq $true) "Operational metrics database.ready must be true before backup."
    Assert-True ($metricsBefore.database.requiredTablesPresent -eq $true) "Required tables must be present before backup."
    $contract = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/contract" -Headers $adminHeaders -TimeoutSec 30
    Assert-True ([int]$contract.currentSchemaVersion -eq 4) "Sync contract schema version must be 4."
    Write-Step "Admin login and observability baseline PASS"

    Write-Step "Production backup source SQL snapshot..."
    $sourceSnapshot = Invoke-DbJsonFile -SqlPath $sqlCheckPath -Variables @{ tenant_id = $TenantId }
    Assert-True ($sourceSnapshot.requiredTablesPresent -eq $true) "Production required tables are not present."
    Assert-True ($sourceSnapshot.tenantExists -eq $true) "Tenant does not exist in production source snapshot."
    Assert-True ($sourceSnapshot.pilot08SourceValidation -eq "GO") "PILOT-08 source snapshot returned NO-GO."
    Write-Step "Production backup source SQL snapshot PASS"

    Write-Step "Create logical schema backup with pg_dump..."
    if (Test-Path $backupPath) { Remove-Item -Force $backupPath }
    $dumpArgs = @("run", "--rm", "postgres:17", "pg_dump", "$DatabaseUrl", "--schema=pos", "--schema-only", "--no-owner", "--no-privileges")
    $global:LASTEXITCODE = 0
    $dumpOutput = docker @dumpArgs
    if ($LASTEXITCODE -ne 0) { throw "pg_dump schema-only backup failed." }
    $global:LASTEXITCODE = 0
    $dumpOutput | Set-Content -Path $backupPath -Encoding UTF8
    $backupContent = Get-Content -Raw -Path $backupPath
    Assert-True ($backupContent.Contains("CREATE SCHEMA")) "Schema backup does not contain CREATE SCHEMA."
    Assert-True ($backupContent.Contains("CREATE TABLE")) "Schema backup does not contain CREATE TABLE."
    Assert-True ($backupContent.Contains("audit_events")) "Schema backup does not contain audit_events."
    $backupSha256 = Get-FileSha256 -Path $backupPath
    $backupBytes = (Get-Item $backupPath).Length
    Assert-True ($backupBytes -gt 10000) "Schema backup file is unexpectedly small."
    Write-Step "Create logical schema backup with pg_dump PASS"

    Write-Step "Restore backup into isolated PostgreSQL container..."
    $containerId = Invoke-DockerCommand -Arguments @("run", "-d", "--name", $restoreContainer, "-e", "POSTGRES_PASSWORD=solidpos", "-e", "POSTGRES_DB=solidpos_restore", "postgres:17") -FailureMessage "Could not start restore PostgreSQL container."
    $containerStarted = $true
    Wait-PostgresContainerReady -ContainerName $restoreContainer
    $copyTarget = "${restoreContainer}:/tmp/pos-schema-backup.sql"
    Invoke-DockerCommandNoOutput -Arguments @("cp", $backupPath, $copyTarget) -FailureMessage "Could not copy backup into restore container."
    $restoreBootstrapSql = "CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public; CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;"
    Invoke-RestorePsql -ContainerName $restoreContainer -PsqlArguments @("-v", "ON_ERROR_STOP=1", "-c", $restoreBootstrapSql) -FailureMessage "Restore extension bootstrap failed."
    Invoke-RestorePsql -ContainerName $restoreContainer -PsqlArguments @("-v", "ON_ERROR_STOP=1", "-f", "/tmp/pos-schema-backup.sql") -FailureMessage "Restore psql execution failed."
    Write-Step "Restore backup into isolated PostgreSQL container PASS"

    Write-Step "Restore schema validation..."
    $restoreQuery = "SELECT json_build_object('schemaExists', to_regnamespace('pos') IS NOT NULL, 'tenantsTable', to_regclass('pos.tenants') IS NOT NULL, 'salesTable', to_regclass('pos.sales') IS NOT NULL, 'paymentsTable', to_regclass('pos.payments') IS NOT NULL, 'syncInboxTable', to_regclass('pos.sync_inbox_events') IS NOT NULL, 'syncConflictsTable', to_regclass('pos.sync_conflicts') IS NOT NULL, 'auditEventsTable', to_regclass('pos.audit_events') IS NOT NULL, 'restoreValidation', CASE WHEN to_regclass('pos.tenants') IS NOT NULL AND to_regclass('pos.sales') IS NOT NULL AND to_regclass('pos.audit_events') IS NOT NULL THEN 'GO' ELSE 'NO-GO' END)::text;"
    $restoreOutput = Invoke-RestorePsqlOutput -ContainerName $restoreContainer -PsqlArguments @("-tA", "-v", "ON_ERROR_STOP=1", "-c", $restoreQuery) -FailureMessage "Restore validation query failed."
    $restoreJson = ($restoreOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1) | ConvertFrom-Json
    Assert-True ($restoreJson.restoreValidation -eq "GO") "Restored schema validation returned NO-GO."
    Write-Step "Restore schema validation PASS"

    Write-Step "Production rollback transaction drill..."
    $rollbackResult = Invoke-DbJsonFile -SqlPath $sqlRollbackPath -Variables @{ tenant_id = $TenantId; trace_id = $rollbackTraceId; entity_id = $rollbackEntityId }
    Assert-True ($rollbackResult.insertVisibleInsideTransaction -eq $true) "Rollback drill insert was not visible inside transaction."
    Assert-True ([long]$rollbackResult.persistedRollbackRows -eq 0) "Rollback drill left persisted rows in production."
    Assert-True ($rollbackResult.rollbackValidation -eq "GO") "Rollback drill returned NO-GO."
    Write-Step "Production rollback transaction drill PASS"

    Write-Step "Post-drill production health and metrics..."
    $readyAfter = Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
    Assert-True ($readyAfter.status -eq "ready") "Production readiness did not return ready after drill."
    Assert-True ($readyAfter.database -eq "ready") "Production database readiness did not return ready after drill."
    $metricsAfter = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
    Assert-True ($metricsAfter.database.ready -eq $true) "Operational metrics database.ready must be true after drill."
    Assert-True ($metricsAfter.database.requiredTablesPresent -eq $true) "Required tables must be present after drill."
    Write-Step "Post-drill production health and metrics PASS"

    Write-Step "Write backup manifest and pilot log..."
    $manifest = [pscustomobject]@{
        tenantId = $TenantId
        baseUrl = $script:base
        generatedAt = (Get-Date).ToUniversalTime().ToString("o")
        backupPath = $backupPath
        backupSha256 = $backupSha256
        backupBytes = $backupBytes
        restoreContainer = $restoreContainer
        sourceSnapshot = $sourceSnapshot
        restoreValidation = $restoreJson
        rollbackTraceId = $rollbackTraceId
        rollbackEntityId = $rollbackEntityId
        rollbackValidation = $rollbackResult
        schemaVersion = [int]$contract.currentSchemaVersion
        goNoGo = "GO"
    }
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -Path $manifestPath -Encoding UTF8
    Set-Content -Path $logPath -Encoding UTF8 -Value "# SolidPOS PILOT-08 Backup Restore Rollback Drill Log"
    Add-Content -Path $logPath -Encoding UTF8 -Value ""
    Add-Content -Path $logPath -Encoding UTF8 -Value "status: PASS REAL PRODUCTION / GO"
    Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
    Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"
    Add-Content -Path $logPath -Encoding UTF8 -Value "backupPath: $backupPath"
    Add-Content -Path $logPath -Encoding UTF8 -Value "backupSha256: $backupSha256"
    Add-Content -Path $logPath -Encoding UTF8 -Value "backupBytes: $backupBytes"
    Add-Content -Path $logPath -Encoding UTF8 -Value "restoreContainer: $restoreContainer"
    Add-Content -Path $logPath -Encoding UTF8 -Value "restoreValidation: $($restoreJson.restoreValidation)"
    Add-Content -Path $logPath -Encoding UTF8 -Value "rollbackTraceId: $rollbackTraceId"
    Add-Content -Path $logPath -Encoding UTF8 -Value "persistedRollbackRows: $($rollbackResult.persistedRollbackRows)"
    Add-Content -Path $logPath -Encoding UTF8 -Value "schemaVersion: $($contract.currentSchemaVersion)"
    Add-Content -Path $logPath -Encoding UTF8 -Value "goNoGo: GO"
    Write-Step "Write backup manifest and pilot log PASS"

    Write-Step "PILOT-08 PASS REAL PRODUCTION / GO"
    [pscustomobject]@{
        tenantId = $TenantId
        baseUrl = $script:base
        backupPath = $backupPath
        backupSha256 = $backupSha256
        backupBytes = $backupBytes
        restoreContainer = $restoreContainer
        restoreValidation = $restoreJson.restoreValidation
        rollbackTraceId = $rollbackTraceId
        persistedRollbackRows = $rollbackResult.persistedRollbackRows
        sourceTenantExists = $sourceSnapshot.tenantExists
        sourceSalesCount = $sourceSnapshot.salesCount
        sourceAuditEventCount = $sourceSnapshot.auditEventCount
        schemaVersion = [int]$contract.currentSchemaVersion
        backupRestoreContract = "pg_dump_schema_restore_rollback"
        goNoGo = "GO"
        message = "SolidPOS PILOT-08 backup restore rollback drill completed."
    }
}
finally {
    if ($containerStarted -and -not $KeepRestoreContainer) {
        docker rm -f $restoreContainer | Out-Null
    }
}
