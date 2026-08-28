param(
    [Parameter(Mandatory = $true)] [string] $BaseUrl,
    [Parameter(Mandatory = $true)] [string] $ProvisionKey,
    [Parameter(Mandatory = $true)] [string] $TenantName,
    [Parameter(Mandatory = $true)] [string] $AdminEmail,
    [Parameter(Mandatory = $true)] [string] $AdminFullName,
    [Parameter(Mandatory = $true)] [string] $AdminPassword,
    [string] $StoreCode = "MAIN",
    [string] $StoreName = "Main Store",
    [string] $Timezone = "America/Hermosillo",
    [string] $Currency = "MXN",
    [string] $BusinessVertical = "cafeteria",
    [string] $UiLayout = "qsr_touch",
    [string] $IdempotencyKey = "solidpos-production-bootstrap"
)

$ErrorActionPreference = "Stop"

$body = @{
    tenantName = $TenantName
    adminEmail = $AdminEmail
    adminFullName = $AdminFullName
    adminPassword = $AdminPassword
    storeCode = $StoreCode
    storeName = $StoreName
    timezone = $Timezone
    currency = $Currency
    businessVertical = $BusinessVertical
    uiLayout = $UiLayout
    idempotencyKey = $IdempotencyKey
    disableDemoUser = $true
} | ConvertTo-Json -Depth 20

$headers = @{
    "X-SolidPOS-Provision-Key" = $ProvisionKey
}

Invoke-RestMethod `
    -Method Post `
    -Uri "$BaseUrl/api/v1/provisioning/tenants/bootstrap" `
    -ContentType "application/json" `
    -Headers $headers `
    -Body $body
