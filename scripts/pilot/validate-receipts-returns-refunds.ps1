param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [string]$StoreCode = "MAIN",
    [string]$ProductSku = "QSR-AMERICANO",
    [string]$PaymentMethodCode = "cash",
    [int64]$OpeningAmountCents = 20000,
    [switch]$SkipDashboardValidation
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[PILOT-04] $Message" }
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
    if ($null -ne $Response.returns) { return @($Response.returns) }
    if ($null -ne $Response.results) { return @($Response.results) }
    return @($Response)
}
function Get-EntityId {
    param($Item)
    if ($null -eq $Item) { return $null }
    if ($null -ne $Item.id) { return [string]$Item.id }
    if ($null -ne $Item.returnId) { return [string]$Item.returnId }
    if ($null -ne $Item.return_id) { return [string]$Item.return_id }
    if ($null -ne $Item.saleId) { return [string]$Item.saleId }
    if ($null -ne $Item.sale_id) { return [string]$Item.sale_id }
    return $null
}
function Find-ReturnInReadModel {
    param([string]$ReturnId, [string]$SaleId, [hashtable]$Headers, [DateTimeOffset]$OccurredAt)
    $from = [Uri]::EscapeDataString($OccurredAt.AddMinutes(-10).ToUniversalTime().ToString("o"))
    $to = [Uri]::EscapeDataString((Get-Date).ToUniversalTime().AddMinutes(10).ToString("o"))
    $queries = @(
        "$script:base/api/v1/returns?saleId=$SaleId&limit=200",
        "$script:base/api/v1/returns?from=$from&to=$to&limit=200",
        "$script:base/api/v1/returns?limit=200"
    )
    foreach ($attempt in 1..5) {
        foreach ($query in $queries) {
            $response = Invoke-RestMethod -Method Get -Uri $query -Headers $Headers -TimeoutSec 30
            $items = Get-ResponseItems $response
            $match = $items | Where-Object { (Get-EntityId $_) -eq $ReturnId } | Select-Object -First 1
            if ($null -ne $match) { return $match }
        }
        $detail = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/returns/$ReturnId" -Headers $Headers -TimeoutSec 30
        if ((Get-EntityId $detail) -eq $ReturnId) { return $detail }
        Start-Sleep -Milliseconds (250 * $attempt)
    }
    throw "Return read model did not include created return. returnId=$ReturnId saleId=$SaleId"
}
function New-ControlledCashSale {
    param(
        [string]$SaleNote,
        [string]$Reference,
        [hashtable]$Headers,
        [string]$ProductId,
        [string]$CashierUserId,
        [int64]$AmountCents,
        [string]$PaymentMethodCode
    )
    $now = (Get-Date).ToUniversalTime().ToString("o")
    $saleBody = @{
      localSaleId = [guid]::NewGuid().ToString()
      cashierUserId = $CashierUserId
      customerId = $null
      occurredAt = $now
      localCreatedAt = $now
      lines = @(@{ productId = $ProductId; variantId = $null; quantity = "1"; discountCents = 0; preparationNote = $SaleNote; modifierIds = @() })
      payments = @(@{ localPaymentId = [guid]::NewGuid().ToString(); methodCode = $PaymentMethodCode; amountCents = $AmountCents; reference = $Reference })
      tipCents = 0
    } | ConvertTo-Json -Depth 8
    $createdSale = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sales" -Headers $Headers -ContentType "application/json" -Body $saleBody -TimeoutSec 30
    Assert-True ($createdSale.status -eq "completed") "Controlled sale was not completed."
    Assert-True ([int64]$createdSale.totalCents -eq $AmountCents) "Controlled sale total mismatch."
    Assert-True ([int64]$createdSale.paidCents -eq $AmountCents) "Controlled sale paid amount mismatch."
    Assert-True ([int64]$createdSale.changeCents -eq 0) "Controlled sale change should be zero."
    return $createdSale
}

$script:base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-04-receipts-returns-refunds-check.sql"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$logPath = Join-Path $logDirectory "pilot-04-receipts-returns-refunds-log.md"

Write-Step "Local repository guardrails..."
Assert-True (Test-Path (Join-Path $repoRoot ".gitignore")) ".gitignore is required before PILOT-04 validation."
Assert-True (Test-Path $sqlPath) "PILOT-04 SQL validator is missing."
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
$metricsDatabaseReady = $false; $metricsRequiredTablesPresent = $false
if ($null -ne $metrics.database) { $metricsDatabaseReady = [bool]$metrics.database.ready; $metricsRequiredTablesPresent = [bool]$metrics.database.requiredTablesPresent }
elseif ($null -ne $metrics.databaseReady) { $metricsDatabaseReady = [bool]$metrics.databaseReady; $metricsRequiredTablesPresent = $true }
Assert-True ($metricsDatabaseReady -eq $true) "Protected metrics did not report database ready true."
Assert-True ($metricsRequiredTablesPresent -eq $true) "Protected metrics did not report required tables present true."
Write-Step "Protected metrics PASS"

Write-Step "Production receipts/returns data lookup via PostgreSQL..."
$storeId = Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' limit 1;"
$adminUserId = Invoke-DbScalar "select id from pos.users where tenant_id = '$TenantId' and email = lower('$Email') and status = 'active' limit 1;"
$productId = Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents = [int64](Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;")
Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) "Store not found for PILOT-04."
Assert-True (-not [string]::IsNullOrWhiteSpace($adminUserId)) "Admin user not found for PILOT-04."
Assert-True (-not [string]::IsNullOrWhiteSpace($productId)) "Product not found for PILOT-04."
Assert-True ($priceCents -gt 0) "Product price not found for PILOT-04."
Write-Step "Production receipts/returns data lookup via PostgreSQL PASS"

Write-Step "Terminal enrollment/register..."
$tokenBody = @{ storeId = $storeId; expiresInMinutes = 30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType "application/json" -Body $tokenBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) "Enrollment token was not returned."
$fingerprint = "pilot-04-receipts-returns-refunds-$TenantId"
$terminalBody = @{ enrollmentToken = $enrollment.enrollmentToken; name = "Pilot 04 Receipts Returns Terminal"; fingerprint = $fingerprint; appVersion = "pilot-04" } | ConvertTo-Json
$terminalSession = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/terminal/register" -ContentType "application/json" -Body $terminalBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.accessToken)) "Terminal register did not return accessToken."
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.terminal.id)) "Terminal register did not return terminal id."
$terminalHeaders = @{ Authorization = "Bearer $($terminalSession.accessToken)" }
Write-Step "Terminal enrollment/register PASS"

Write-Step "Closing stale open PILOT-04 cash shifts..."
Invoke-DbNonQuery "UPDATE pos.cash_shifts cs SET status = 'closed', closed_by_user_id = '$adminUserId', counted_cash_cents = cs.expected_cash_cents, difference_cents = 0, closed_at = now(), updated_at = now() FROM pos.terminals t WHERE cs.tenant_id = '$TenantId' AND cs.terminal_id = t.id AND t.tenant_id = cs.tenant_id AND t.fingerprint = '$fingerprint' AND cs.status = 'open';"
Write-Step "Closing stale open PILOT-04 cash shifts PASS"

Write-Step "Opening cash shift..."
$openBody = @{ storeId = $storeId; terminalId = $terminalSession.terminal.id; openedByUserId = $adminUserId; openingAmountCents = $OpeningAmountCents } | ConvertTo-Json
$shift = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType "application/json" -Body $openBody -TimeoutSec 30
Assert-True ($shift.status -eq "open") "Cash shift did not open."
Write-Step "Opening cash shift PASS"

Write-Step "Creating controlled sale for receipt/return..."
$sale = New-ControlledCashSale -SaleNote "PILOT-04 venta base para recibo y devolución" -Reference "PILOT-04-SALE" -Headers $terminalHeaders -ProductId $productId -CashierUserId $adminUserId -AmountCents $priceCents -PaymentMethodCode $PaymentMethodCode
$saleLineId = $null
if ($null -ne $sale.lines -and $sale.lines.Count -gt 0) { $saleLineId = [string]$sale.lines[0].id }
if ([string]::IsNullOrWhiteSpace($saleLineId)) { $saleLineId = Invoke-DbScalar "select id from pos.sale_lines where tenant_id = '$TenantId' and sale_id = '$($sale.id)' order by line_number limit 1;" }
Assert-True (-not [string]::IsNullOrWhiteSpace($saleLineId)) "Sale line id not found for return."
Write-Step "Creating controlled sale for receipt/return PASS"

Write-Step "Validating protected receipt..."
$basicReceipt = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/$($sale.id)" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($basicReceipt.saleId -eq $sale.id) "Protected sale receipt saleId mismatch."
Assert-True ([int64]$basicReceipt.totalCents -eq $priceCents) "Protected receipt total mismatch."
Assert-True (@($basicReceipt.lines).Count -ge 1) "Protected receipt did not include lines."
Assert-True (@($basicReceipt.payments).Count -ge 1) "Protected receipt did not include payments."
Write-Step "Validating protected receipt PASS"

Write-Step "Issuing and validating digital receipt..."
$receipt = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/receipts/$($sale.id)/issue" -Headers $adminHeaders -ContentType "application/json" -Body "{}" -TimeoutSec 30
Assert-True ($receipt.saleId -eq $sale.id) "Digital receipt saleId mismatch."
Assert-True ($receipt.status -eq "active") "Digital receipt status mismatch."
Assert-True (-not [string]::IsNullOrWhiteSpace($receipt.publicToken)) "Digital receipt public token missing."
$protectedReceipt = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/$($sale.id)/digital" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($protectedReceipt.id -eq $receipt.id) "Protected digital receipt id mismatch."
$publicReceipt = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/public/$($receipt.publicToken)" -TimeoutSec 30
Assert-True ($publicReceipt.id -eq $receipt.id) "Public digital receipt id mismatch."
$emailBody = @{ recipientEmail = "pilot04+receipt@solidpos.local" } | ConvertTo-Json
$emailReceipt = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/receipts/$($sale.id)/email" -Headers $adminHeaders -ContentType "application/json" -Body $emailBody -TimeoutSec 30
Assert-True ($emailReceipt.saleId -eq $sale.id) "Email receipt response saleId mismatch."
$acceptedEmailStatuses = @("queued", "queued_stub", "stub_queued", "sent")
Assert-True ($emailReceipt.status -in $acceptedEmailStatuses) "Email receipt status unexpected: $($emailReceipt.status)"
Write-Step "Issuing and validating digital receipt PASS"

Write-Step "Creating full cash return/refund..."
$returnOccurredAt = (Get-Date).ToUniversalTime()
$returnBody = @{
    localReturnId = [guid]::NewGuid().ToString()
    saleId = $sale.id
    createdByUserId = $adminUserId
    reason = "PILOT-04 devolución completa controlada"
    occurredAt = $returnOccurredAt.ToString("o")
    lines = @(@{ saleLineId = $saleLineId; quantity = "1" })
    refunds = @(@{ methodCode = $PaymentMethodCode; amountCents = $priceCents; reference = "PILOT-04-REFUND" })
} | ConvertTo-Json -Depth 8
$return = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/returns" -Headers $terminalHeaders -ContentType "application/json" -Body $returnBody -TimeoutSec 30
Assert-True ($return.status -eq "completed") "Return status was not completed."
Assert-True ($return.saleId -eq $sale.id) "Return saleId mismatch."
Assert-True ([int64]$return.totalCents -eq $priceCents) "Return total mismatch."
Assert-True ([int64]$return.refundCents -eq $priceCents) "Return refund mismatch."
Assert-True (@($return.lines).Count -ge 1) "Return did not include lines."
Assert-True (@($return.refunds).Count -ge 1) "Return did not include refunds."
Assert-True (@($return.inventoryMovements).Count -ge 1) "Return did not include inventory compensation movements."
Write-Step "Creating full cash return/refund PASS"

Write-Step "Validating return detail and read model..."
$returnDetail = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/returns/$($return.id)" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($returnDetail.id -eq $return.id) "Return detail id mismatch."
$returnReadModel = Find-ReturnInReadModel -ReturnId $return.id -SaleId $sale.id -Headers $adminHeaders -OccurredAt $returnOccurredAt
Assert-True ($null -ne $returnReadModel) "Return read model did not include created return."
$saleAfterReturn = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sales/$($sale.id)" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($saleAfterReturn.status -eq "returned") "Sale status after full return was not returned."
Write-Step "Validating return detail and read model PASS"

Write-Step "Validating shift summary and closing shift..."
$summaryAfterReturn = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([int64]$summaryAfterReturn.expectedCashCents -eq $OpeningAmountCents) "Expected cash after cash refund should return to opening amount."
$closeBody = @{ closedByUserId = $adminUserId; countedCashCents = $summaryAfterReturn.expectedCashCents } | ConvertTo-Json
$closedShift = Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/close" -Headers $terminalHeaders -ContentType "application/json" -Body $closeBody -TimeoutSec 30
Assert-True ($closedShift.status -eq "closed") "Cash shift did not close."
Assert-True ([int64]$closedShift.differenceCents -eq 0) "Cash shift difference was not zero."
$summaryAfterClose = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ($summaryAfterClose.status -eq "closed") "Closed shift summary did not return closed."
Assert-True ([int64]$summaryAfterClose.expectedCashCents -eq $OpeningAmountCents) "Closed shift expected cash mismatch."
Assert-True ([int64]$summaryAfterClose.differenceCents -eq 0) "Closed shift difference mismatch."
Write-Step "Validating shift summary and closing shift PASS"

Write-Step "Validating receipt/return audit trail..."
$receiptAudit = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?entityId=$($receipt.id)&limit=20" -Headers $adminHeaders -TimeoutSec 30
$receiptAuditItems = Get-ResponseItems $receiptAudit
Assert-True ($null -ne ($receiptAuditItems | Where-Object { $_.action -eq "receipt.issued" -and $_.entityId -eq $receipt.id } | Select-Object -First 1)) "Audit did not include receipt.issued."
Assert-True ($null -ne ($receiptAuditItems | Where-Object { $_.action -eq "receipt.email_stub_queued" -and $_.entityId -eq $receipt.id } | Select-Object -First 1)) "Audit did not include receipt.email_stub_queued."
$returnAudit = Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?entityId=$($return.id)&limit=20" -Headers $adminHeaders -TimeoutSec 30
$returnAuditItems = Get-ResponseItems $returnAudit
Assert-True ($null -ne ($returnAuditItems | Where-Object { $_.action -eq "return.created" -and $_.entityId -eq $return.id } | Select-Object -First 1)) "Audit did not include return.created."
Write-Step "Validating receipt/return audit trail PASS"

Write-Step "Validating receipts/returns/refunds persistence via PostgreSQL..."
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
    -v sale_id="$($sale.id)" `
    -v sale_line_id="$saleLineId" `
    -v return_id="$($return.id)" `
    -v receipt_id="$($receipt.id)" `
    -v expected_total_cents="$priceCents" `
    -v expected_refund_cents="$priceCents" `
    -v expected_cash_cents="$($summaryAfterClose.expectedCashCents)" `
    -v counted_cash_cents="$($summaryAfterClose.countedCashCents)" `
    -v difference_cents="$($summaryAfterClose.differenceCents)" `
    -v payment_method_code="$PaymentMethodCode" `
    -f scripts/pilot/pilot-04-receipts-returns-refunds-check.sql 2>&1
$sqlOutput | Write-Host
if ($LASTEXITCODE -ne 0) { throw "PILOT-04 receipts/returns/refunds SQL validation failed." }
$sqlText = ($sqlOutput | Out-String)
Assert-True ($sqlText -match "PILOT-04 receipts returns refunds validation PASS") "PILOT-04 SQL assertion did not report PASS."
Assert-True ($sqlText -match "GO") "PILOT-04 SQL assertion did not report GO."
$global:LASTEXITCODE = 0
Write-Step "Validating receipts/returns/refunds persistence via PostgreSQL PASS"

Write-Step "Pilot receipts/returns/refunds log initialized..."
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$log = @"
# SolidPOS PILOT-04 Receipts / Returns / Refunds Log

- Date UTC: $((Get-Date).ToUniversalTime().ToString("o"))
- TenantId: $TenantId
- StoreCode: $StoreCode
- TerminalId: $($terminalSession.terminal.id)
- ShiftId: $($shift.id)
- SaleId: $($sale.id)
- SaleLineId: $saleLineId
- ReceiptId: $($receipt.id)
- ReceiptNumber: $($receipt.receiptNumber)
- PublicReceipt: passed
- ProtectedReceipt: passed
- EmailReceiptStub: passed
- ReturnId: $($return.id)
- ReturnStatus: $($return.status)
- RefundCents: $($return.refundCents)
- ProductSku: $ProductSku
- PaymentMethodCode: $PaymentMethodCode
- OpeningAmountCents: $OpeningAmountCents
- ExpectedCashCents: $($summaryAfterClose.expectedCashCents)
- CountedCashCents: $($summaryAfterClose.countedCashCents)
- DifferenceCents: $($summaryAfterClose.differenceCents)
- GO/NO-GO: GO

## Operator Notes

- A controlled cash sale was created.
- Protected receipt and digital receipt were validated.
- Public receipt and email receipt stub were validated.
- A full cash return/refund was created against the sale line.
- Inventory compensation, refund movement, audit trail and PostgreSQL persistence were validated.
- Cash shift closed with zero difference after refund.
"@
Set-Content -Path $logPath -Value $log -Encoding UTF8
Write-Step "Pilot receipts/returns/refunds log initialized PASS"

[pscustomobject]@{
    tenantId = $TenantId
    baseUrl = $script:base
    adminEmail = $Email
    storeCode = $StoreCode
    terminalId = $terminalSession.terminal.id
    shiftId = $shift.id
    saleId = $sale.id
    saleLineId = $saleLineId
    receiptId = $receipt.id
    receiptNumber = $receipt.receiptNumber
    returnId = $return.id
    returnStatus = $return.status
    productSku = $ProductSku
    paymentMethodCode = $PaymentMethodCode
    saleTotalCents = $priceCents
    refundCents = $return.refundCents
    expectedCashCents = $summaryAfterClose.expectedCashCents
    countedCashCents = $summaryAfterClose.countedCashCents
    differenceCents = $summaryAfterClose.differenceCents
    protectedReceipt = "passed"
    publicReceipt = "passed"
    emailReceiptStub = "passed"
    returnReadModel = "passed"
    receiptReturnSqlValidation = "passed"
    receiptReturnLog = "docs/pilot/logs/pilot-04-receipts-returns-refunds-log.md"
    goNoGo = "GO"
    message = "SolidPOS PILOT-04 receipts returns refunds validation completed."
} | Format-List
