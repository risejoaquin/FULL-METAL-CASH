param(
    [Parameter(Mandatory = $true)] [string] $BaseUrl,
    [Parameter(Mandatory = $true)] [string] $TenantId,
    [Parameter(Mandatory = $true)] [string] $AdminEmail,
    [Parameter(Mandatory = $true)] [string] $AdminPassword
)

$ErrorActionPreference = "Stop"

$loginBody = @{
    email = $AdminEmail
    password = $AdminPassword
    tenantId = $TenantId
} | ConvertTo-Json

$session = Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/api/v1/auth/login" `
    -ContentType "application/json" `
    -Body $loginBody

$headers = @{ Authorization = "Bearer $($session.accessToken)" }

Invoke-RestMethod `
    -Method Get `
    -Uri "$BaseUrl/api/v1/tenants/current" `
    -Headers $headers
