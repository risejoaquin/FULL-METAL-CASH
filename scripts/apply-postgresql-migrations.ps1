param(
    [string]$DatabaseUrl = $env:DATABASE_URL,
    [string]$DockerContainer = "solidpos-postgres",
    [string]$DockerDatabase = "solidpos",
    [string]$DockerUser = "solidpos",
    [string]$DockerPassword = "solidpos_dev_password",
    [switch]$ResetSchema
)

$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$MigrationFiles = @(
    "database/postgresql/001_initial_schema_postgresql.sql",
    "database/postgresql/002_seed_permissions.sql",
    "database/postgresql/003_seed_mvp_defaults.sql",
    "database/postgresql/005_sync_push_runtime.sql",
    "database/postgresql/006_sync_processing_runtime.sql",
    "database/postgresql/007_modifier_inventory_semantics.sql",
    "database/postgresql/008_digital_receipts_runtime.sql",
    "database/postgresql/009_returns_refunds_runtime.sql",
    "database/postgresql/010_customers_runtime.sql",
    "database/postgresql/011_discounts_promotions_runtime.sql",
    "database/postgresql/012_inventory_control_hardening.sql",
    "database/postgresql/013_sync_conflict_resolution_runtime.sql",
    "database/postgresql/014_builder_updates_runtime.sql"
)

function Invoke-PostgresScalar {
    param([string]$Sql)

    $psql = Get-Command psql -ErrorAction SilentlyContinue
    if ($psql -and -not [string]::IsNullOrWhiteSpace($DatabaseUrl)) {
        $result = & psql $DatabaseUrl -tAc $Sql
        if ($LASTEXITCODE -ne 0) {
            throw "psql scalar command failed"
        }
        return ($result | Select-Object -First 1).Trim()
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        throw "Neither psql nor docker is available. Install PostgreSQL client tools or start the included Docker environment."
    }

    $result = & docker exec -e "PGPASSWORD=$DockerPassword" $DockerContainer psql -U $DockerUser -d $DockerDatabase -tAc $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "docker psql scalar command failed"
    }
    return ($result | Select-Object -First 1).Trim()
}

function Invoke-PostgresSql {
    param([string]$Sql)

    $psql = Get-Command psql -ErrorAction SilentlyContinue
    if ($psql -and -not [string]::IsNullOrWhiteSpace($DatabaseUrl)) {
        & psql $DatabaseUrl -v ON_ERROR_STOP=1 -c $Sql
        if ($LASTEXITCODE -ne 0) {
            throw "psql command failed"
        }
        return
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        throw "Neither psql nor docker is available. Install PostgreSQL client tools or start the included Docker environment."
    }

    & docker exec -e "PGPASSWORD=$DockerPassword" $DockerContainer psql -U $DockerUser -d $DockerDatabase -v ON_ERROR_STOP=1 -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "docker psql command failed"
    }
}

function Invoke-PostgresFile {
    param([string]$RelativePath)

    $FullPath = Join-Path $RootDir $RelativePath
    if (-not (Test-Path $FullPath)) {
        throw "Migration file not found: $FullPath"
    }

    $psql = Get-Command psql -ErrorAction SilentlyContinue
    if ($psql -and -not [string]::IsNullOrWhiteSpace($DatabaseUrl)) {
        & psql $DatabaseUrl -v ON_ERROR_STOP=1 -f $FullPath
        if ($LASTEXITCODE -ne 0) {
            throw "psql failed for $RelativePath"
        }
        return
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        throw "Neither psql nor docker is available. Install PostgreSQL client tools or start the included Docker environment."
    }

    $ContainerPath = "/tmp/solidpos_$(Split-Path $RelativePath -Leaf)"
    & docker cp $FullPath "${DockerContainer}:$ContainerPath"
    if ($LASTEXITCODE -ne 0) {
        throw "docker cp failed for $RelativePath"
    }

    & docker exec -e "PGPASSWORD=$DockerPassword" $DockerContainer psql -U $DockerUser -d $DockerDatabase -v ON_ERROR_STOP=1 -f $ContainerPath
    if ($LASTEXITCODE -ne 0) {
        throw "docker psql failed for $RelativePath"
    }
}

if ($ResetSchema) {
    Write-Host "Resetting PostgreSQL schema pos"
    Invoke-PostgresSql "DROP SCHEMA IF EXISTS pos CASCADE;"
}

$SchemaExists = Invoke-PostgresScalar "SELECT to_regclass('pos.tenants') IS NOT NULL;"
if ($SchemaExists -eq "t" -and -not $ResetSchema) {
    Write-Host "Existing pos schema detected. Skipping 001_initial_schema_postgresql.sql. Use -ResetSchema to rebuild local schema."
    $MigrationFiles = @(
        "database/postgresql/002_seed_permissions.sql",
        "database/postgresql/003_seed_mvp_defaults.sql",
        "database/postgresql/005_sync_push_runtime.sql",
        "database/postgresql/006_sync_processing_runtime.sql",
        "database/postgresql/007_modifier_inventory_semantics.sql",
        "database/postgresql/008_digital_receipts_runtime.sql",
        "database/postgresql/009_returns_refunds_runtime.sql",
    "database/postgresql/010_customers_runtime.sql",
    "database/postgresql/011_discounts_promotions_runtime.sql",
    "database/postgresql/012_inventory_control_hardening.sql",
    "database/postgresql/013_sync_conflict_resolution_runtime.sql",
    "database/postgresql/014_builder_updates_runtime.sql"
    )
}

foreach ($MigrationFile in $MigrationFiles) {
    Write-Host "Applying $MigrationFile"
    Invoke-PostgresFile $MigrationFile
}

Write-Host "PostgreSQL migrations applied."
