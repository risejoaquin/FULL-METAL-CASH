param(
    [string]$Url = "http://localhost:5000",
    [string]$PostgresConnectionString = "Host=localhost;Port=5432;Database=solidpos;Username=solidpos;Password=solidpos_dev_password",
    [string]$JwtSigningKey = "dev-only-solidpos-signing-key-change-before-production"
)

$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$ProjectPath = Join-Path $RootDir "src/PosServer/SolidPOS.PosServer.Api/SolidPOS.PosServer.Api.csproj"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = $Url
$env:ConnectionStrings__Postgres = $PostgresConnectionString
$env:Jwt__SigningKey = $JwtSigningKey

Write-Host "Starting SolidPOS PosServer"
Write-Host "Environment: $env:ASPNETCORE_ENVIRONMENT"
Write-Host "URL: $env:ASPNETCORE_URLS"
Write-Host "PostgreSQL: configured"
Write-Host "JWT signing key: configured"

dotnet run --project $ProjectPath
