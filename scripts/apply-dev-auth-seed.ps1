param(
    [string]$DatabaseUrl = $env:DATABASE_URL,
    [string]$DockerContainer = "solidpos-postgres",
    [string]$DockerDatabase = "solidpos",
    [string]$DockerUser = "solidpos",
    [string]$DockerPassword = "solidpos_dev_password"
)

$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$SeedFile = Join-Path $RootDir "database/postgresql/004_seed_dev_auth.sql"

if (-not (Test-Path $SeedFile)) {
    throw "Seed file not found: $SeedFile"
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($psql -and -not [string]::IsNullOrWhiteSpace($DatabaseUrl)) {
    & psql $DatabaseUrl -v ON_ERROR_STOP=1 -f $SeedFile
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed while applying dev auth seed"
    }

    Write-Host "Development auth seed applied."
    return
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) {
    throw "Neither psql nor docker is available. Install PostgreSQL client tools or start the included Docker environment."
}

$ContainerPath = "/tmp/solidpos_004_seed_dev_auth.sql"
& docker cp $SeedFile "${DockerContainer}:$ContainerPath"
if ($LASTEXITCODE -ne 0) {
    throw "docker cp failed while copying dev auth seed"
}

& docker exec -e "PGPASSWORD=$DockerPassword" $DockerContainer psql -U $DockerUser -d $DockerDatabase -v ON_ERROR_STOP=1 -f $ContainerPath
if ($LASTEXITCODE -ne 0) {
    throw "docker psql failed while applying dev auth seed"
}

Write-Host "Development auth seed applied."
