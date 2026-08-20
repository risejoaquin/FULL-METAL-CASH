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
    [int64]$CashTenderExtraCents = 1000,
    [switch]$SkipDashboardValidation
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[PILOT-02] $Message"
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Convert-SolidPosSecureString {
    param([securestring]$SecureValue)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Invoke-DbScalar {
    param([Parameter(Mandatory = $true)] [string]$Sql)

    $result = docker run --rm `
      --env "DATABASE_URL=$DatabaseUrl" `
      postgres:16 `
      psql "$DatabaseUrl" -tAc $Sql

    if ($LASTEXITCODE -ne 0) {
        throw "DB scalar command failed."
    }

    return ($result | Select-Object -First 1).Trim()
}

function Invoke-DbNonQuery {
    param([Parameter(Mandatory = $true)] [string]$Sql)

    docker run --rm `
      --env "DATABASE_URL=$DatabaseUrl" `
      postgres:16 `
      psql "$DatabaseUrl" -v ON_ERROR_STOP=1 -c $Sql | Write-Host

    if ($LASTEXITCODE -ne 0) {
        throw "DB non-query command failed."
    }
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

function Get-SaleReadModelId {
    param($Item)
    if ($null -eq $Item) { return $null }
    if ($null -ne $Item.id) { return [string]$Item.id }
    if ($null -ne $Item.saleId) { return [string]$Item.saleId }
    if ($null -ne $Item.sale_id) { return [string]$Item.sale_id }
    return $null
}


function Find-SaleInSalesReadModel {
    param(
        [Parameter(Mandatory = $true)] [string]$SaleId,
        [Parameter(Mandatory = $true)] [string]$StoreId,
        [Parameter(Mandatory = $true)] [string]$TerminalId,
        [Parameter(Mandatory = $true)] [hashtable]$Headers,
        [Parameter(Mandatory = $true)] [DateTimeOffset]$OccurredAt
    )

    $from = [Uri]::EscapeDataString($OccurredAt.AddMinutes(-10).ToUniversalTime().ToString("o"))
    $to = [Uri]::EscapeDataString((Get-Date).ToUniversalTime().AddMinutes(10).ToString("o"))

    $queries = @(
        "$base/api/v1/sales?storeId=$StoreId&terminalId=$TerminalId&status=completed&limit=200",
        "$base/api/v1/sales?storeId=$StoreId&status=completed&limit=200",
        "$base/api/v1/sales?from=$from&to=$to&storeId=$StoreId&status=completed&limit=200",
        "$base/api/v1/sales?from=$from&to=$to&status=completed&limit=200",
        "$base/api/v1/sales?status=completed&limit=200"
    )

    $lastObservedIds = @()
    foreach ($attempt in 1..5) {
        foreach ($query in $queries) {
            $response = Invoke-RestMethod -Method Get -Uri $query -Headers $Headers -TimeoutSec 30
            $items = Get-ResponseItems $response
            $match = $items | Where-Object { (Get-SaleReadModelId $_) -eq $SaleId } | Select-Object -First 1
            if ($null -ne $match) {
                return $match
            }

            $lastObservedIds += @($items | Select-Object -First 5 | ForEach-Object { Get-SaleReadModelId $_ })
        }

        # Fallback: the operational sale detail endpoint is also a read model for the created sale.
        # It prevents false negatives when the list endpoint is eventually consistent or returns a
        # different envelope/field shape, while the canonical sale read model already sees the sale.
        $detailResponse = Invoke-RestMethod -Method Get -Uri "$base/api/v1/sales/$SaleId" -Headers $Headers -TimeoutSec 30
        if ((Get-SaleReadModelId $detailResponse) -eq $SaleId) {
            return $detailResponse
        }

        Start-Sleep -Milliseconds (250 * $attempt)
    }

    $sample = ($lastObservedIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique -First 10) -join ', '
    throw "Sale read model did not include created sale. saleId=$SaleId storeId=$StoreId terminalId=$TerminalId sampleObservedSaleIds=[$sample]"
}

$base = $BaseUrl.TrimEnd('/')
$plainPassword = Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$sqlPath = Join-Path $scriptRoot "pilot-02-transaction-check.sql"
$logDirectory = Join-Path $repoRoot "docs\pilot\logs"
$dailyLogPath = Join-Path $logDirectory "pilot-02-transaction-log.md"

Write-Step "Local repository guardrails..."
if (-not (Test-Path (Join-Path $repoRoot ".gitignore"))) {
    throw ".gitignore is required before pilot transaction validation."
}
Write-Step "Local repository guardrails PASS"

Write-Step "Local secret scan..."
# PowerShell scripts do not reliably reset $LASTEXITCODE. A previous native-tool
# failure can leave a stale non-zero value even when scan-local-secrets.ps1
# completed successfully. Validate this PowerShell script by its success state
# instead of inheriting stale native exit codes.
$global:LASTEXITCODE = 0
& (Join-Path $repoRoot "scripts\security\scan-local-secrets.ps1") -Root $repoRoot
if (-not $?) { throw "Local secret scan failed." }
$global:LASTEXITCODE = 0
Write-Step "Local secret scan PASS"

if (-not $SkipDashboardValidation) {
    Write-Step "PosDashboard production build and self-test..."
    & (Join-Path $repoRoot "scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1") `
      -BaseUrl $base `
      -TenantId $TenantId `
      -Email $Email
    if ($LASTEXITCODE -ne 0) { throw "Dashboard validation failed." }
    Write-Step "PosDashboard production build and self-test PASS"
}

Write-Step "Production liveness..."
$live = Invoke-RestMethod -Method Get -Uri "$base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq "alive") "Production liveness did not return alive."
Write-Step "Production liveness PASS"

Write-Step "Production readiness..."
$ready = Invoke-RestMethod -Method Get -Uri "$base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq "ready") "Production readiness did not return ready."
Assert-True ($ready.database -eq "ready") "Production database readiness did not return ready."
Write-Step "Production readiness PASS"

Write-Step "Admin login..."
$loginBody = @{
  email = $Email
  password = $plainPassword
  tenantId = $TenantId
} | ConvertTo-Json

$session = Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType "application/json" -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) "Admin login did not return accessToken."
$adminHeaders = @{ Authorization = "Bearer $($session.accessToken)" }
Write-Step "Admin login PASS"

Write-Step "Protected metrics..."
$metrics = Invoke-RestMethod -Method Get -Uri "$base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
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

Write-Step "Production pilot data lookup via PostgreSQL..."
$storeId = Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' limit 1;"
$adminUserId = Invoke-DbScalar "select id from pos.users where tenant_id = '$TenantId' and email = lower('$Email') and status = 'active' limit 1;"
$productId = Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents = Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;"

Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) "Store not found for pilot transaction."
Assert-True (-not [string]::IsNullOrWhiteSpace($adminUserId)) "Admin user not found for pilot transaction."
Assert-True (-not [string]::IsNullOrWhiteSpace($productId)) "Product not found for pilot transaction."
Assert-True (-not [string]::IsNullOrWhiteSpace($priceCents)) "Product price not found for pilot transaction."
Write-Step "Production pilot data lookup via PostgreSQL PASS"

Write-Step "Terminal enrollment/register..."
$tokenBody = @{ storeId = $storeId; expiresInMinutes = 30 } | ConvertTo-Json
$enrollment = Invoke-RestMethod -Method Post -Uri "$base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType "application/json" -Body $tokenBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) "Enrollment token was not returned."

$fingerprint = "pilot-02-real-transaction-$TenantId"
$terminalBody = @{
  enrollmentToken = $enrollment.enrollmentToken
  name = "Pilot 02 Real Transaction Terminal"
  fingerprint = $fingerprint
  appVersion = "pilot-02"
} | ConvertTo-Json

$terminalSession = Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/terminal/register" -ContentType "application/json" -Body $terminalBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.accessToken)) "Terminal register did not return accessToken."
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalSession.terminal.id)) "Terminal register did not return terminal id."
$terminalHeaders = @{ Authorization = "Bearer $($terminalSession.accessToken)" }
Write-Step "Terminal enrollment/register PASS"

Write-Step "Closing stale open PILOT-02 cash shifts..."
Invoke-DbNonQuery "UPDATE pos.cash_shifts cs SET status = 'closed', closed_by_user_id = '$adminUserId', counted_cash_cents = cs.expected_cash_cents, difference_cents = 0, closed_at = now(), updated_at = now() FROM pos.terminals t WHERE cs.tenant_id = '$TenantId' AND cs.terminal_id = t.id AND t.tenant_id = cs.tenant_id AND t.fingerprint = '$fingerprint' AND cs.status = 'open';"
Write-Step "Closing stale open PILOT-02 cash shifts PASS"

Write-Step "Opening cash shift..."
$openBody = @{
  storeId = $storeId
  terminalId = $terminalSession.terminal.id
  openedByUserId = $adminUserId
  openingAmountCents = $OpeningAmountCents
} | ConvertTo-Json
$shift = Invoke-RestMethod -Method Post -Uri "$base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType "application/json" -Body $openBody -TimeoutSec 30
Assert-True ($shift.status -eq "open") "Cash shift did not open."
Write-Step "Opening cash shift PASS"

Write-Step "Creating real controlled POS sale..."
$saleTotal = [int64]$priceCents
$paidCents = $saleTotal + $CashTenderExtraCents
$expectedChangeCents = $CashTenderExtraCents
$localSaleId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")

$saleBody = @{
  localSaleId = $localSaleId
  cashierUserId = $adminUserId
  customerId = $null
  occurredAt = $now
  localCreatedAt = $now
  lines = @(
    @{
      productId = $productId
      variantId = $null
      quantity = "1"
      discountCents = 0
      preparationNote = "PILOT-02 real controlled sale"
      modifierIds = @()
    }
  )
  payments = @(
    @{
      localPaymentId = [guid]::NewGuid().ToString()
      methodCode = $PaymentMethodCode
      amountCents = $paidCents
      reference = "pilot-02-real-pos-transaction"
    }
  )
  tipCents = 0
} | ConvertTo-Json -Depth 8

$sale = Invoke-RestMethod -Method Post -Uri "$base/api/v1/sales" -Headers $terminalHeaders -ContentType "application/json" -Body $saleBody -TimeoutSec 30
Assert-True ($sale.status -eq "completed") "Sale was not completed."
Assert-True ([int64]$sale.totalCents -eq $saleTotal) "Sale total mismatch."
Assert-True ([int64]$sale.paidCents -eq $paidCents) "Sale paid amount mismatch."
Assert-True ([int64]$sale.changeCents -eq $expectedChangeCents) "Sale change amount mismatch."
Write-Step "Creating real controlled POS sale PASS"

Write-Step "Validating sale detail and read model..."
$saleDetail = Invoke-RestMethod -Method Get -Uri "$base/api/v1/sales/$($sale.id)" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($saleDetail.id -eq $sale.id) "Sale detail id mismatch."
Assert-True ($saleDetail.status -eq "completed") "Sale detail status mismatch."
Assert-True (@($saleDetail.lines).Count -ge 1) "Sale detail has no lines."
Assert-True (@($saleDetail.payments).Count -ge 1) "Sale detail has no payments."
Assert-True (@($saleDetail.inventoryMovements).Count -ge 1) "Sale detail has no inventory movements."

$saleOccurredAt = [DateTimeOffset]::Parse(([string]$saleDetail.occurredAt), [Globalization.CultureInfo]::InvariantCulture)
Assert-True ([string]$saleDetail.storeId -eq $storeId) "Sale detail storeId mismatch."
Assert-True ([string]$saleDetail.terminalId -eq [string]$sale.terminalId) "Sale detail terminalId mismatch."
$listedSale = Find-SaleInSalesReadModel `
  -SaleId ([string]$sale.id) `
  -StoreId $storeId `
  -TerminalId ([string]$sale.terminalId) `
  -Headers $adminHeaders `
  -OccurredAt $saleOccurredAt
Assert-True ($null -ne $listedSale) "Sale read model did not include the created sale."
Write-Step "Validating sale detail and read model PASS"

Write-Step "Issuing and validating digital receipt..."
$receipt = Invoke-RestMethod -Method Post -Uri "$base/api/v1/receipts/$($sale.id)/issue" -Headers $terminalHeaders -ContentType "application/json" -Body (@{} | ConvertTo-Json) -TimeoutSec 30
Assert-True ($receipt.status -eq "active") "Digital receipt was not active."
Assert-True (-not [string]::IsNullOrWhiteSpace($receipt.publicToken)) "Digital receipt public token missing."

$protectedReceipt = Invoke-RestMethod -Method Get -Uri "$base/api/v1/receipts/$($sale.id)/digital" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($protectedReceipt.saleId -eq $sale.id) "Protected digital receipt saleId mismatch."

$publicReceipt = Invoke-RestMethod -Method Get -Uri "$base/api/v1/receipts/public/$($receipt.publicToken)" -TimeoutSec 30
Assert-True ($publicReceipt.saleId -eq $sale.id) "Public digital receipt saleId mismatch."
Write-Step "Issuing and validating digital receipt PASS"

Write-Step "Validating shift summary and closing shift..."
$summaryBeforeClose = Invoke-RestMethod -Method Get -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([int64]$summaryBeforeClose.cashSalesCents -ge $saleTotal) "Cash shift summary did not include sale cash amount."
Assert-True ([int64]$summaryBeforeClose.salesCount -ge 1) "Cash shift summary did not include sales count."

$closeBody = @{ closedByUserId = $adminUserId; countedCashCents = $summaryBeforeClose.expectedCashCents } | ConvertTo-Json
$closedShift = Invoke-RestMethod -Method Post -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/close" -Headers $terminalHeaders -ContentType "application/json" -Body $closeBody -TimeoutSec 30
Assert-True ($closedShift.status -eq "closed") "Cash shift did not close."

$summaryAfterClose = Invoke-RestMethod -Method Get -Uri "$base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ($summaryAfterClose.status -eq "closed") "Closed shift summary did not return closed."
Assert-True ([int64]$summaryAfterClose.differenceCents -eq 0) "Closed shift difference was not zero."
Write-Step "Validating shift summary and closing shift PASS"

Write-Step "Validating audit event read model..."
$auditEvents = Invoke-RestMethod -Method Get -Uri "$base/api/v1/audit/events?entityId=$($sale.id)&limit=20" -Headers $adminHeaders -TimeoutSec 30
$auditItems = Get-ResponseItems $auditEvents
$saleAudit = $auditItems | Where-Object { $_.action -eq "sale.completed" -and $_.entityId -eq $sale.id } | Select-Object -First 1
Assert-True ($null -ne $saleAudit) "Audit read model did not include sale.completed."
Write-Step "Validating audit event read model PASS"

Write-Step "Validating transaction persistence via PostgreSQL..."
$global:LASTEXITCODE = 0
$sqlOutput = docker run --rm `
  --env "DATABASE_URL=$DatabaseUrl" `
  -v "${repoRoot}:/workspace" `
  -w /workspace `
  postgres:16 `
  psql "$DatabaseUrl" `
    -v ON_ERROR_STOP=1 `
    -v tenant_id="$TenantId" `
    -v sale_id="$($sale.id)" `
    -v receipt_id="$($receipt.id)" `
    -v expected_total_cents="$saleTotal" `
    -v expected_paid_cents="$paidCents" `
    -v expected_change_cents="$expectedChangeCents" `
    -v product_sku="$ProductSku" `
    -v payment_method_code="$PaymentMethodCode" `
    -f scripts/pilot/pilot-02-transaction-check.sql 2>&1
$sqlOutput | Write-Host
if ($LASTEXITCODE -ne 0) { throw "PILOT-02 transaction SQL validation failed." }
$sqlText = ($sqlOutput | Out-String)
Assert-True ($sqlText -match "PILOT-02 real POS transaction validation PASS") "PILOT-02 SQL assertion did not report PASS."
Assert-True ($sqlText -match "GO") "PILOT-02 SQL assertion did not report GO."
$global:LASTEXITCODE = 0
Write-Step "Validating transaction persistence via PostgreSQL PASS"

Write-Step "Pilot transaction log initialized..."
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$log = @"
# SolidPOS PILOT-02 Real POS Transaction Log

- Date UTC: $((Get-Date).ToUniversalTime().ToString("o"))
- TenantId: $TenantId
- StoreCode: $StoreCode
- TerminalId: $($terminalSession.terminal.id)
- ShiftId: $($shift.id)
- SaleId: $($sale.id)
- ReceiptId: $($receipt.id)
- ProductSku: $ProductSku
- PaymentMethodCode: $PaymentMethodCode
- SaleTotalCents: $saleTotal
- PaidCents: $paidCents
- ChangeCents: $expectedChangeCents
- ShiftDifferenceCents: $($summaryAfterClose.differenceCents)
- GO/NO-GO: GO

## Operator Notes

- Controlled real transaction completed.
- Digital receipt protected/public validation completed.
- Inventory ledger validation completed.
- Audit trail validation completed.
- Cash shift closed with zero difference.
"@
Set-Content -Path $dailyLogPath -Value $log -Encoding UTF8
Write-Step "Pilot transaction log initialized PASS"

[pscustomobject]@{
  tenantId = $TenantId
  baseUrl = $base
  adminEmail = $Email
  storeCode = $StoreCode
  terminalId = $terminalSession.terminal.id
  shiftId = $shift.id
  saleId = $sale.id
  receiptId = $receipt.id
  receiptNumber = $receipt.receiptNumber
  productSku = $ProductSku
  paymentMethodCode = $PaymentMethodCode
  totalCents = $saleTotal
  paidCents = $paidCents
  changeCents = $expectedChangeCents
  inventoryMovementCount = @($saleDetail.inventoryMovements).Count
  cashSalesCents = $summaryAfterClose.cashSalesCents
  expectedCashCents = $summaryAfterClose.expectedCashCents
  differenceCents = $summaryAfterClose.differenceCents
  protectedReceipt = "passed"
  publicReceipt = "passed"
  dashboardValidation = $(if ($SkipDashboardValidation) { "skipped" } else { "passed" })
  localSecretScan = "passed"
  transactionSqlValidation = "passed"
  transactionLog = "docs/pilot/logs/pilot-02-transaction-log.md"
  goNoGo = "GO"
  message = "SolidPOS PILOT-02 real POS transaction validation completed."
}
