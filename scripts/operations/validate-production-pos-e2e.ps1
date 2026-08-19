param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$AdminEmail,
    [Parameter(Mandatory = $true)] [string]$AdminPassword,
    [string]$StoreCode = "MAIN",
    [string]$ProductSku = "QSR-AMERICANO",
    [string]$PaymentMethodCode = "cash",
    [string]$DatabaseUrl = $env:DATABASE_URL
)

$ErrorActionPreference = "Stop"

function Invoke-DbScalar {
    param([Parameter(Mandatory = $true)] [string]$Sql)

    if ([string]::IsNullOrWhiteSpace($DatabaseUrl)) {
        throw "DATABASE_URL is required for E2E lookup."
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
$adminUserId = Invoke-DbScalar "select id from pos.users where tenant_id = '$TenantId' and email = lower('$AdminEmail') and status = 'active' limit 1;"
$productId = Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents = Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;"

if ([string]::IsNullOrWhiteSpace($storeId) -or [string]::IsNullOrWhiteSpace($adminUserId) -or [string]::IsNullOrWhiteSpace($productId) -or [string]::IsNullOrWhiteSpace($priceCents)) {
    throw "Missing operational seed data. Run scripts/operations/seed-production-pos-runtime.ps1 first."
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

$fingerprint = "iteration-02-e2e-$TenantId"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Iteration 02 E2E Terminal"
  fingerprint = $fingerprint
  appVersion = "iteration-02"
} | ConvertTo-Json

$terminalSession = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/auth/terminal/register" `
  -ContentType "application/json" `
  -Body $terminalBody

$terminalHeaders = @{ Authorization = "Bearer $($terminalSession.accessToken)" }

$openBody = @{
  storeId = $storeId
  terminalId = $terminalSession.terminal.id
  openedByUserId = $adminUserId
  openingAmountCents = 10000
} | ConvertTo-Json

$shift = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $openBody

$saleTotal = [int64]$priceCents
$saleBody = @{
  localSaleId = [guid]::NewGuid().ToString()
  cashierUserId = $adminUserId
  customerId = $null
  occurredAt = (Get-Date).ToUniversalTime().ToString("o")
  localCreatedAt = (Get-Date).ToUniversalTime().ToString("o")
  lines = @(
    @{
      productId = $productId
      variantId = $null
      quantity = "1"
      discountCents = 0
      preparationNote = "Iteration 02 E2E"
      modifierIds = @()
    }
  )
  payments = @(
    @{
      localPaymentId = [guid]::NewGuid().ToString()
      methodCode = $PaymentMethodCode
      amountCents = $saleTotal
      reference = "iteration-02-e2e"
    }
  )
  tipCents = 0
} | ConvertTo-Json -Depth 8

$sale = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/sales" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $saleBody

$receipt = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/receipts/$($sale.id)/issue" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body (@{} | ConvertTo-Json)

$summaryBeforeClose = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/summary" `
  -Headers $terminalHeaders

$closeBody = @{
  closedByUserId = $adminUserId
  countedCashCents = $summaryBeforeClose.expectedCashCents
} | ConvertTo-Json

$closedShift = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/close" `
  -Headers $terminalHeaders `
  -ContentType "application/json" `
  -Body $closeBody

$summaryAfterClose = Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/summary" `
  -Headers $terminalHeaders

[pscustomobject]@{
  tenantId = $TenantId
  storeId = $storeId
  terminalId = $terminalSession.terminal.id
  shiftId = $shift.id
  saleId = $sale.id
  receiptId = $receipt.id
  receiptNumber = $receipt.receiptNumber
  shiftStatus = $closedShift.status
  cashSalesCents = $summaryAfterClose.cashSalesCents
  expectedCashCents = $summaryAfterClose.expectedCashCents
  differenceCents = $summaryAfterClose.differenceCents
  message = "Production POS E2E flow completed."
}
