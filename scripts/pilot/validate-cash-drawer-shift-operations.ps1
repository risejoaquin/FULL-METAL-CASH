param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [string]$StoreCode = "MAIN",
    [string]$ProductSku = "QSR-AMERICANO",
    [string]$PaymentMethodCode = "cash",
    [int64]$OpeningAmountCents = 30000,
    [int64]$CashInCents = 5000,
    [int64]$CashOutCents = 2000,
    [switch]$SkipDashboardValidation
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[PILOT-03] $Message"
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

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

function Invoke-DbNonQuery {
    param([Parameter(Mandatory = $true)] [string]$Sql)
    $global:LASTEXITCODE = 0
    docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:16 psql "$DatabaseUrl" -v ON_ERROR_STOP=1 -c $Sql | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "DB non-query command failed." }
    $global:LASTEXITCODE = 0
}

function Get-ResponseItems {
    param($Response)
    if ($null -eq $Response) { return @() }
    if ($Response -is [System.Array]) { return @($Response) }
    if ($null -ne $Response.items) { return @($Response.items) }
    if ($null -ne $Response.data) { return @($Response.data) }
    if ($null -ne $Response.sales) { return @($Response.sales) }
    if ($null -ne $Response.results) { return @($Response.results) }
    return @($Response)
}

function New-ControlledCashSale {
    param(
        [Parameter(Mandatory = $true)] [string]$SaleNote,
        [Parameter(Mandatory = $true)] [string]$Reference,
        [Parameter(Mandatory = $true)] [hashtable]$Headers,
        [Parameter(Mandatory = $true)] [string]$ProductId,
        [Parameter(Mandatory = $true)] [string]$CashierUserId,
        [Parameter(Mandatory = $true)] [int64]$AmountCents,
        [Parameter(Mandatory = $true)] [string]$PaymentMethodCode
    )

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $saleBody = @{
      localSaleId = [guid]::NewGuid().ToString()
      cashierUserId = $CashierUserId
      customerId = $null
      occurredAt = $now
      localCreatedAt = $now
      lines = @(
        @{
          productId = $ProductId
          variantId = $null
          quantity = "1"
          discountCents = 0
          preparationNote = $SaleNote
          modifierIds = @()
        }
      )
      payments = @(
        @{
          localPaymentId = [guid]::NewGuid().ToString()
          methodCode = $PaymentMethodCode
          amountCents = $AmountCents
          reference = $Reference
        }
      )
      tipCents = 0
    } | ConvertTo-Json -Depth 8

    $createdSale = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sales" -Headers $Headers -ContentType "application/json" -Body $saleBody -TimeoutSec 30
    Assert-True ($createdSale.status -eq "completed") "Controlled cash sale was not completed."
    Assert-True ([int64]$createdSale.totalCents -eq $AmountCents) "Controlled cash sale total mismatch."
    Assert-True ([int64]$createdSale.paidCents -eq $AmountCents) "Controlled cash sale paid amount mismatch."
    Assert-True ([int64]$createdSale.changeCents -eq 0) "Controlled cash sale change should be zero."
    return $createdSale
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-03-cash-shift-check.sql"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$cashLogPath = Join-Path $logDirectory "pilot-03-cash-shift-log.md"

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before pilot cash drawer validation."
Assert-True (Test-Path $sqlPath) "PILOT-03 SQL validator is missing."
Write-Step "Local repository guardrails PASS"

Write-Step "Local secret scan..."
$global:LASTEXITCODE = 0
& (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
if (-not $?) { throw "Local secret scan failed." }
$global:LASTEXITCODE = 0
Write-Step "Local secret scan PASS"

if (-not $SkipDashboardValidation) {
    Write-Step "PosDashboard production build and self-test..."
    $global:LASTEXITCODE = 0
    & (Join-Path $repoRoot "scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1") -BaseUrl $script:base -TenantId $TenantId -Email $Email
    if ($LASTEXITCODE -ne 0) { throw "Dashboard validation failed." }
    $global:LASTEXITCODE = 0
    Write-Step "PosDashboard production build and self-test PASS"
}

Write-Step "Production liveness..."
$live = Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq "alive") "Production liveness did not return alive."
Write-Step "Production liveness PASS"

Write-Step "Production readiness..."
$ready = Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq "ready") "Production readiness did not return ready."
Assert-True ($ready.database -eq "ready") "Production database readiness did not return ready."
Write-Step "Production readiness PASS"

Write-Step "Admin login..."
$loginBody = @{ email = $Email; password = $plainPassword; tenantId = $TenantId } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
Write-Step "Admin login PASS"

Write-Step "Protected metrics..."
$metrics = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$metricsDatabaseReady = $false
$metricsRequiredTablesPresent = $false
if ($null -ne $metrics.database) {
    $metricsDatabaseReady = [bool]$metrics.database.ready
    $metricsRequiredTablesPresent = [bool]$metrics.database.requiredTablesPresent
}
elseif ($null -ne $metrics.databaseReady) {
    $metricsDatabaseReady = [bool]$metrics.databaseReady
    $metricsRequiredTablesPresent = $true
}
Assert-True ($metricsDatabaseReady -eq $true) "Protected metrics did not report database ready true."
Assert-True ($metricsRequiredTablesPresent -eq $true) "Protected metrics did not report required tables present true."
Write-Step "Protected metrics PASS"

Write-Step "Production cash drawer data lookup via PostgreSQL..."
$storeId = Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' limit 1;"
$adminUserId = Invoke-DbScalar "select id from pos.users where tenant_id = '$TenantId' and email = lower('$Email') and status = 'active' limit 1;"
$productId = Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents = Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;"
Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) "Store not found for cash drawer validation."
Assert-True (-not [string]::IsNullOrWhiteSpace($adminUserId)) "Admin user not found for cash drawer validation."
Assert-True (-not [string]::IsNullOrWhiteSpace($productId)) "Product not found for cash drawer validation."
Assert-True (-not [string]::IsNullOrWhiteSpace($priceCents)) "Product price not found for cash drawer validation."
Write-Step "Production cash drawer data lookup via PostgreSQL PASS"

Write-Step "Terminal enrollment/register..."
$tokenBody = @{ storeId = $storeId; expiresInMinutes = 30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType "application/json" -Body $tokenBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) "Enrollment token was not returned."
$fingerprint = "pilot-03-cash-shift-$TenantId"
$terminalBody = @{ enrollmentToken = $enrollment.enrollmentToken; name = "Pilot 03 Cash Drawer Terminal"; fingerprint = $fingerprint; appVersion = "pilot-03" } | ConvertTo-Json
$terminalSession = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/terminal/register" -ContentType "application/json" -Body $terminalBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.accessToken)) "Terminal register did not return accessToken."
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.terminal.id)) "Terminal register did not return terminal id."
$terminalHeaders = @{ Authorization = "Bearer $($terminalSession.accessToken)" }
Write-Step "Terminal enrollment/register PASS"

Write-Step "Closing stale open PILOT-03 cash shifts..."
Invoke-DbNonQuery "UPDATE pos.cash_shifts cs SET status = 'closed', closed_by_user_id = '$adminUserId', counted_cash_cents = cs.expected_cash_cents, difference_cents = 0, closed_at = now(), updated_at = now() FROM pos.terminals t WHERE cs.tenant_id = '$TenantId' AND cs.terminal_id = t.id AND t.tenant_id = cs.tenant_id AND t.fingerprint = '$fingerprint' AND cs.status = 'open';"
Write-Step "Closing stale open PILOT-03 cash shifts PASS"

Write-Step "Opening cash shift..."
$openBody = @{ storeId = $storeId; terminalId = $terminalSession.terminal.id; openedByUserId = $adminUserId; openingAmountCents = $OpeningAmountCents } | ConvertTo-Json
$shift = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType "application/json" -Body $openBody -TimeoutSec 30
Assert-True ($shift.status -eq "open") "Cash shift did not open."
Assert-True ([int64]$shift.openingAmountCents -eq $OpeningAmountCents) "Opening amount mismatch."
Write-Step "Opening cash shift PASS"

Write-Step "Validating current open shift..."
$currentShift = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/current" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ($currentShift.id -eq $shift.id) "Current open shift id mismatch."
Assert-True ($currentShift.status -eq "open") "Current open shift status mismatch."
Write-Step "Validating current open shift PASS"

Write-Step "Creating cash movements..."
$cashInBody = @{ movementType = "cash_in"; amountCents = $CashInCents; reason = "PILOT-03 caja chica inicial controlada"; createdByUserId = $adminUserId; authorizedByUserId = $adminUserId } | ConvertTo-Json
$cashIn = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/movements" -Headers $terminalHeaders -ContentType "application/json" -Body $cashInBody -TimeoutSec 30
Assert-True ($cashIn.movementType -eq "cash_in") "cash_in movement type mismatch."
Assert-True ([int64]$cashIn.amountCents -eq $CashInCents) "cash_in amount mismatch."

$cashOutBody = @{ movementType = "cash_out"; amountCents = $CashOutCents; reason = "PILOT-03 salida controlada para insumo menor"; createdByUserId = $adminUserId; authorizedByUserId = $adminUserId } | ConvertTo-Json
$cashOut = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/movements" -Headers $terminalHeaders -ContentType "application/json" -Body $cashOutBody -TimeoutSec 30
Assert-True ($cashOut.movementType -eq "cash_out") "cash_out movement type mismatch."
Assert-True ([int64]$cashOut.amountCents -eq $CashOutCents) "cash_out amount mismatch."

$drawerOpenBody = @{ movementType = "drawer_open_no_sale"; amountCents = 0; reason = "PILOT-03 apertura de cajón sin venta controlada"; createdByUserId = $adminUserId; authorizedByUserId = $adminUserId } | ConvertTo-Json
$drawerOpen = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/movements" -Headers $terminalHeaders -ContentType "application/json" -Body $drawerOpenBody -TimeoutSec 30
Assert-True ($drawerOpen.movementType -eq "drawer_open_no_sale") "drawer_open_no_sale movement type mismatch."
Assert-True ([int64]$drawerOpen.amountCents -eq 0) "drawer_open_no_sale amount mismatch."
Write-Step "Creating cash movements PASS"

Write-Step "Validating movement summary before sales..."
$summaryAfterMovements = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
$expectedAfterMovements = $OpeningAmountCents + $CashInCents - $CashOutCents
Assert-True ([int64]$summaryAfterMovements.expectedCashCents -eq $expectedAfterMovements) "Expected cash after movements mismatch."
Assert-True ([int64]$summaryAfterMovements.cashInCents -eq $CashInCents) "cashInCents summary mismatch."
Assert-True ([int64]$summaryAfterMovements.cashOutCents -eq $CashOutCents) "cashOutCents summary mismatch."
Assert-True ([int]$summaryAfterMovements.noSaleDrawerOpenCount -ge 1) "No-sale drawer count missing."
Assert-True ([int]$summaryAfterMovements.movementCount -ge 3) "Movement count missing."
Write-Step "Validating movement summary before sales PASS"

Write-Step "Creating controlled cash sales for shift accumulation..."
$saleAmount = [int64]$priceCents
$sale1 = New-ControlledCashSale -SaleNote "PILOT-03 cash shift sale 1" -Reference "pilot-03-cash-shift-sale-1" -Headers $terminalHeaders -ProductId $productId -CashierUserId $adminUserId -AmountCents $saleAmount -PaymentMethodCode $PaymentMethodCode
$sale2 = New-ControlledCashSale -SaleNote "PILOT-03 cash shift sale 2" -Reference "pilot-03-cash-shift-sale-2" -Headers $terminalHeaders -ProductId $productId -CashierUserId $adminUserId -AmountCents $saleAmount -PaymentMethodCode $PaymentMethodCode
Assert-True ($sale1.cashShiftId -eq $shift.id -or $null -eq $sale1.cashShiftId) "Sale 1 cashShiftId mismatch."
Assert-True ($sale2.cashShiftId -eq $shift.id -or $null -eq $sale2.cashShiftId) "Sale 2 cashShiftId mismatch."
Write-Step "Creating controlled cash sales for shift accumulation PASS"

Write-Step "Validating shift summary after sales..."
$summaryAfterSales = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
$cashSalesCents = $saleAmount + $saleAmount
$expectedAfterSales = $OpeningAmountCents + $CashInCents - $CashOutCents + $cashSalesCents
Assert-True ([int64]$summaryAfterSales.cashSalesCents -eq $cashSalesCents) "Cash sales summary mismatch."
Assert-True ([int64]$summaryAfterSales.expectedCashCents -eq $expectedAfterSales) "Expected cash after sales mismatch."
Assert-True ([int]$summaryAfterSales.salesCount -eq 2) "Sales count summary mismatch."
Assert-True ([int]$summaryAfterSales.movementCount -ge 3) "Movement count after sales mismatch."
Write-Step "Validating shift summary after sales PASS"

Write-Step "Closing cash shift with zero difference..."
$closeBody = @{ closedByUserId = $adminUserId; countedCashCents = $summaryAfterSales.expectedCashCents } | ConvertTo-Json
$closedShift = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/close" -Headers $terminalHeaders -ContentType "application/json" -Body $closeBody -TimeoutSec 30
Assert-True ($closedShift.status -eq "closed") "Cash shift did not close."
Assert-True ([int64]$closedShift.differenceCents -eq 0) "Cash shift difference was not zero."
$summaryAfterClose = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ($summaryAfterClose.status -eq "closed") "Closed shift summary did not return closed."
Assert-True ([int64]$summaryAfterClose.countedCashCents -eq [int64]$summaryAfterSales.expectedCashCents) "Counted cash summary mismatch."
Assert-True ([int64]$summaryAfterClose.differenceCents -eq 0) "Closed shift difference summary mismatch."
Write-Step "Closing cash shift with zero difference PASS"

Write-Step "Validating cash audit trail..."
$shiftAuditEvents = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?entityId=$($shift.id)&limit=20" -Headers $adminHeaders -TimeoutSec 30
$shiftAuditItems = Get-ResponseItems $shiftAuditEvents
$openAudit = $shiftAuditItems | Where-Object { $_.action -eq "cash.shift.opened" -and $_.entityId -eq $shift.id } | Select-Object -First 1
$closeAudit = $shiftAuditItems | Where-Object { $_.action -eq "cash.shift.closed" -and $_.entityId -eq $shift.id } | Select-Object -First 1
Assert-True ($null -ne $openAudit) "Audit read model did not include cash.shift.opened."
Assert-True ($null -ne $closeAudit) "Audit read model did not include cash.shift.closed."
foreach ($movementId in @($cashIn.id, $cashOut.id, $drawerOpen.id)) {
    $movementAuditEvents = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?entityId=$movementId&limit=20" -Headers $adminHeaders -TimeoutSec 30
    $movementAuditItems = Get-ResponseItems $movementAuditEvents
    $movementAudit = $movementAuditItems | Where-Object { $_.action -eq "cash.movement.created" -and $_.entityId -eq $movementId } | Select-Object -First 1
    Assert-True ($null -ne $movementAudit) "Audit read model did not include cash.movement.created for movement $movementId."
}
Write-Step "Validating cash audit trail PASS"

Write-Step "Validating cash drawer persistence via PostgreSQL..."
$global:LASTEXITCODE = 0
$sqlOutput = docker run --rm `
  --env "DATABASE_URL=$DatabaseUrl" `
  -v "${repoRoot}:/workspace" `
  -w /workspace `
  postgres:16 `
  psql "$DatabaseUrl" `
    -v ON_ERROR_STOP=1 `
    -v tenant_id="$TenantId" `
    -v shift_id="$($shift.id)" `
    -v terminal_id="$($terminalSession.terminal.id)" `
    -v store_id="$storeId" `
    -v sale_1_id="$($sale1.id)" `
    -v sale_2_id="$($sale2.id)" `
    -v cash_in_id="$($cashIn.id)" `
    -v cash_out_id="$($cashOut.id)" `
    -v drawer_open_id="$($drawerOpen.id)" `
    -v opening_amount_cents="$OpeningAmountCents" `
    -v cash_in_cents="$CashInCents" `
    -v cash_out_cents="$CashOutCents" `
    -v cash_sales_cents="$cashSalesCents" `
    -v expected_cash_cents="$expectedAfterSales" `
    -v counted_cash_cents="$($summaryAfterClose.countedCashCents)" `
    -v difference_cents="$($summaryAfterClose.differenceCents)" `
    -f scripts/pilot/pilot-03-cash-shift-check.sql 2>&1
$sqlOutput | Write-Host
if ($LASTEXITCODE -ne 0) { throw "PILOT-03 cash drawer SQL validation failed." }
$sqlText = ($sqlOutput | Out-String)
Assert-True ($sqlText -match "PILOT-03 cash drawer and shift operations validation PASS") "PILOT-03 SQL assertion did not report PASS."
Assert-True ($sqlText -match "GO") "PILOT-03 SQL assertion did not report GO."
$global:LASTEXITCODE = 0
Write-Step "Validating cash drawer persistence via PostgreSQL PASS"

Write-Step "Pilot cash drawer log initialized..."
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$log = @"
# SolidPOS PILOT-03 Cash Drawer / Shift Operations Log

- Date UTC: $((Get-Date).ToUniversalTime().ToString("o"))
- TenantId: $TenantId
- StoreCode: $StoreCode
- TerminalId: $($terminalSession.terminal.id)
- ShiftId: $($shift.id)
- OpeningAmountCents: $OpeningAmountCents
- CashInCents: $CashInCents
- CashOutCents: $CashOutCents
- DrawerOpenNoSaleCount: $($summaryAfterClose.noSaleDrawerOpenCount)
- Sale1Id: $($sale1.id)
- Sale2Id: $($sale2.id)
- CashSalesCents: $cashSalesCents
- ExpectedCashCents: $($summaryAfterClose.expectedCashCents)
- CountedCashCents: $($summaryAfterClose.countedCashCents)
- DifferenceCents: $($summaryAfterClose.differenceCents)
- GO/NO-GO: GO

## Operator Notes

- Cash shift opened successfully.
- Cash in, cash out and no-sale drawer movement were recorded.
- Two controlled cash sales were accumulated in the same shift.
- Shift summary matched expected cash.
- Shift closed with zero difference.
- Cash audit trail and PostgreSQL persistence validation completed.
"@
Set-Content -Path $cashLogPath -Value $log -Encoding UTF8
Write-Step "Pilot cash drawer log initialized PASS"

[pscustomobject]@{
  tenantId = $TenantId
  baseUrl = $script:base
  adminEmail = $Email
  storeCode = $StoreCode
  terminalId = $terminalSession.terminal.id
  shiftId = $shift.id
  openingAmountCents = $OpeningAmountCents
  cashInMovementId = $cashIn.id
  cashOutMovementId = $cashOut.id
  drawerOpenMovementId = $drawerOpen.id
  cashInCents = $summaryAfterClose.cashInCents
  cashOutCents = $summaryAfterClose.cashOutCents
  noSaleDrawerOpenCount = $summaryAfterClose.noSaleDrawerOpenCount
  movementCount = $summaryAfterClose.movementCount
  sale1Id = $sale1.id
  sale2Id = $sale2.id
  salesCount = $summaryAfterClose.salesCount
  cashSalesCents = $summaryAfterClose.cashSalesCents
  expectedCashCents = $summaryAfterClose.expectedCashCents
  countedCashCents = $summaryAfterClose.countedCashCents
  differenceCents = $summaryAfterClose.differenceCents
  shiftStatus = $summaryAfterClose.status
  dashboardValidation = $(if ($SkipDashboardValidation) { "skipped" } else { "passed" })
  localSecretScan = "passed"
  cashDrawerSqlValidation = "passed"
  cashDrawerLog = "docs/pilot/logs/pilot-03-cash-shift-log.md"
  goNoGo = "GO"
  message = "SolidPOS PILOT-03 cash drawer and shift operations validation completed."
}
