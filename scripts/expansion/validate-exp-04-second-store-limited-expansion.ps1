
param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [string]$StoreCodePrefix = "EXP04",
    [string]$ProductSku = "QSR-AMERICANO",
    [string]$PaymentMethodCode = "cash",
    [int64]$OpeningAmountCents = 25000,
    [int64]$CashTenderExtraCents = 500,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-04] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','stores')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function Get-EntityId { param($Object) if($null -eq $Object){return $null}; foreach($n in @('id','saleId','sale_id','receiptId','terminalId','shiftId','storeId')){ if($null -ne $Object.$n){ return [string]$Object.$n } }; return $null }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-DbScalar { param([string]$Sql) $global:LASTEXITCODE=0; $output=docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:17 psql "$DatabaseUrl" -tA -v ON_ERROR_STOP=1 -c $Sql; if($LASTEXITCODE -ne 0){throw "DB scalar command failed."}; $global:LASTEXITCODE=0; return ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1).Trim() }
function Invoke-DbNonQuery { param([string]$Sql) $global:LASTEXITCODE=0; docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:17 psql "$DatabaseUrl" -v ON_ERROR_STOP=1 -c $Sql | Write-Host; if($LASTEXITCODE -ne 0){throw "DB non-query command failed."}; $global:LASTEXITCODE=0 }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
function Find-SaleInSalesReadModel { param([string]$SaleId,[string]$StoreId,[string]$TerminalId,[hashtable]$Headers,[DateTimeOffset]$OccurredAt) $from=[Uri]::EscapeDataString($OccurredAt.AddMinutes(-10).ToUniversalTime().ToString('o')); $to=[Uri]::EscapeDataString((Get-Date).ToUniversalTime().AddMinutes(10).ToString('o')); $queries=@("$script:base/api/v1/sales?storeId=$StoreId&terminalId=$TerminalId&status=completed&limit=200","$script:base/api/v1/sales?from=$from&to=$to&storeId=$StoreId&terminalId=$TerminalId&status=completed&limit=200","$script:base/api/v1/sales?storeId=$StoreId&status=completed&limit=200"); foreach($attempt in 1..5){ foreach($query in $queries){ $items=Get-Items (Invoke-RestMethod -Method Get -Uri $query -Headers $Headers -TimeoutSec 30); $match=$items | Where-Object { (Get-EntityId $_) -eq $SaleId } | Select-Object -First 1; if($null -ne $match){ return $match } }; Start-Sleep -Milliseconds (250*$attempt) }; throw "Sale read model did not include created sale. saleId=$SaleId storeId=$StoreId terminalId=$TerminalId" }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-04-second-store-expansion-check.sql'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-04-second-store-limited-expansion'
$manifestPath=Join-Path $runtimeDirectory 'second-store-expansion-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-04-second-store-limited-expansion-log.md'
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-04 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-04 document contract...'
$docs=@(
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-04-second-store-limited-expansion.md'; terms=@('exp-04','second store','limited expansion','go/no-go','rollback') },
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-04-second-store-operator-checklist.md'; terms=@('before','during','after','store','evidence') },
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-04-second-store-rollback.md'; terms=@('rollback','store','terminal','cash shift','sale','containment') },
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-04-second-store-go-no-go.md'; terms=@('go','no-go','blocker','condition','exp-05') }
)
foreach($doc in $docs){ Assert-DocumentContains -Path $doc.path -Terms $doc.terms }
Write-Step 'EXP-04 document contract PASS'

Write-Step 'Local secret scan...'
Invoke-CheckedCommand -Name 'secret scan' -Command { & (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -Root $repoRoot }
Write-Step 'Local secret scan PASS'
Write-Step 'dotnet restore...'; Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet restore' -Command { & dotnet restore $slnPath } } finally { Pop-Location }; Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...'; Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet build' -Command { & dotnet build $slnPath --no-restore } } finally { Pop-Location }; Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...'; Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet test' -Command { & dotnet test $slnPath --no-build } } finally { Pop-Location }; Write-Step 'dotnet test PASS'

Write-Step 'Production liveness/readiness...'
$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30; Assert-True ($live.status -eq 'alive') 'Production liveness did not return alive.'
$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30; Assert-True ($ready.status -eq 'ready') 'Production readiness did not return ready.'; Assert-True ($ready.database -eq 'ready') 'Production database readiness did not return ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and expansion pre-check...'
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $($session.accessToken)"}
$metricsBefore=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($metricsBefore.database.ready -eq $true) 'Protected metrics database.ready must be true.'; Assert-True ($metricsBefore.database.requiredTablesPresent -eq $true) 'Protected metrics requiredTablesPresent must be true.'
Assert-True ($null -ne (Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30)) 'Sync status endpoint returned null.'
Assert-True ($null -ne (Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30)) 'Pending conflicts endpoint returned null.'
Write-Step 'Admin login and expansion pre-check PASS'

Write-Step 'Production data lookup...'
$adminUserId=Invoke-DbScalar "select id from pos.users where tenant_id = '$TenantId' and email = lower('$Email') and status = 'active' and deleted_at is null limit 1;"
$productId=Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents=[int64](Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;")
$storeCountBefore=[int64](Invoke-DbScalar "select count(*) from pos.stores where tenant_id = '$TenantId' and deleted_at is null;")
Assert-True (-not [string]::IsNullOrWhiteSpace($adminUserId)) 'Admin user not found for EXP-04.'; Assert-True (-not [string]::IsNullOrWhiteSpace($productId)) 'Product not found for EXP-04.'; Assert-True ($priceCents -gt 0) 'Product price not found for EXP-04.'
Write-Step 'Production data lookup PASS'

Write-Step 'Create second store and admin store access...'
$storeCode=("{0}-{1}" -f $StoreCodePrefix, ([guid]::NewGuid().ToString('N').Substring(0,8))).ToUpperInvariant()
$store=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/stores" -Headers $adminHeaders -ContentType 'application/json' -Body (@{code=$storeCode;name='EXP-04 Second Store Limited Expansion';address='EXP-04 controlled second store';phone=$null;status='active'}|ConvertTo-Json) -TimeoutSec 30
$storeId=[string]$store.id; Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) 'Second store create did not return id.'; Assert-True ([string]$store.code -eq $storeCode) 'Second store code mismatch.'; Assert-True ([string]$store.status -eq 'active') 'Second store status is not active.'
Invoke-DbNonQuery "INSERT INTO pos.user_store_access (tenant_id, user_id, store_id) VALUES ('$TenantId', '$adminUserId', '$storeId') ON CONFLICT DO NOTHING;"
$storeListMatch=Get-Items (Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/stores" -Headers $adminHeaders -TimeoutSec 30) | Where-Object { [string]$_.id -eq $storeId } | Select-Object -First 1
Assert-True ($null -ne $storeListMatch) 'Second store was not visible in stores list.'
$storeAccessCount=[int64](Invoke-DbScalar "select count(*) from pos.user_store_access where tenant_id = '$TenantId' and user_id = '$adminUserId' and store_id = '$storeId';"); Assert-True ($storeAccessCount -ge 1) 'Admin store access was not assigned to second store.'
$storeCountAfterCreate=[int64](Invoke-DbScalar "select count(*) from pos.stores where tenant_id = '$TenantId' and deleted_at is null;"); Assert-True ($storeCountAfterCreate -ge ($storeCountBefore + 1)) 'Store count did not increase after EXP-04 store creation.'
Write-Step 'Create second store and admin store access PASS'

Write-Step 'Second store initial terminal enrollment/register...'
$terminalCountBefore=[int64](Invoke-DbScalar "select count(*) from pos.terminals where tenant_id = '$TenantId' and store_id = '$storeId' and deleted_at is null;")
$enrollment=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType 'application/json' -Body (@{storeId=$storeId;expiresInMinutes=30}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) 'Enrollment token was not returned.'
$fingerprint="exp-04-second-store-terminal-$([guid]::NewGuid())"
$terminalSession=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/terminal/register" -ContentType 'application/json' -Body (@{enrollmentToken=$enrollment.enrollmentToken;name='EXP-04 Second Store Initial Terminal';fingerprint=$fingerprint;appVersion='exp-04'}|ConvertTo-Json) -TimeoutSec 30
$terminalId=[string]$terminalSession.terminal.id; $terminalAccessToken=[string]$terminalSession.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalId)) 'Terminal register did not return terminal.id.'; Assert-True (-not [string]::IsNullOrWhiteSpace($terminalAccessToken)) 'Terminal register did not return accessToken.'
$terminalHeaders=@{Authorization="Bearer $terminalAccessToken"}
Write-Step 'Second store initial terminal enrollment/register PASS'

Write-Step 'Bootstrap sync and store isolation check...'
$bootstrap=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/bootstrap" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([string]$bootstrap.tenantId -eq $TenantId) 'Bootstrap tenant mismatch.'; Assert-True ([string]$bootstrap.storeId -eq $storeId) 'Bootstrap store mismatch.'; Assert-True ([string]$bootstrap.terminalId -eq $terminalId) 'Bootstrap terminal mismatch.'
$terminalCountAfterRegister=[int64](Invoke-DbScalar "select count(*) from pos.terminals where tenant_id = '$TenantId' and store_id = '$storeId' and deleted_at is null;"); Assert-True ($terminalCountAfterRegister -ge ($terminalCountBefore + 1)) 'Terminal count did not increase after second store enrollment.'
Write-Step 'Bootstrap sync and store isolation check PASS'

Write-Step 'Open second store independent cash shift...'
Invoke-DbNonQuery "UPDATE pos.cash_shifts SET status = 'closed', counted_cash_cents = expected_cash_cents, difference_cents = 0, closed_at = now(), updated_at = now() WHERE tenant_id = '$TenantId' AND terminal_id = '$terminalId' AND status = 'open';"
$shift=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType 'application/json' -Body (@{storeId=$storeId;terminalId=$terminalId;openedByUserId=$adminUserId;openingAmountCents=$OpeningAmountCents}|ConvertTo-Json) -TimeoutSec 30
Assert-True ($shift.status -eq 'open') 'Second store cash shift did not open.'; Assert-True ([string]$shift.storeId -eq $storeId) 'Cash shift storeId mismatch.'
Write-Step 'Open second store independent cash shift PASS'

Write-Step 'Create controlled sale from second store...'
$saleTotal=$priceCents; $paidCents=$saleTotal + $CashTenderExtraCents; $expectedChangeCents=$CashTenderExtraCents; $now=(Get-Date).ToUniversalTime().ToString('o')
$saleBody=@{localSaleId=[guid]::NewGuid().ToString();cashierUserId=$adminUserId;customerId=$null;occurredAt=$now;localCreatedAt=$now;lines=@(@{productId=$productId;variantId=$null;quantity='1';discountCents=0;preparationNote='EXP-04 second store controlled sale';modifierIds=@()});payments=@(@{localPaymentId=[guid]::NewGuid().ToString();methodCode=$PaymentMethodCode;amountCents=$paidCents;reference='exp-04-second-store-limited-expansion'});tipCents=0}|ConvertTo-Json -Depth 8
$sale=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sales" -Headers $terminalHeaders -ContentType 'application/json' -Body $saleBody -TimeoutSec 30
Assert-True ($sale.status -eq 'completed') 'Second store sale was not completed.'; Assert-True ([int64]$sale.totalCents -eq $saleTotal) 'Second store sale total mismatch.'; Assert-True ([int64]$sale.paidCents -eq $paidCents) 'Second store sale paid mismatch.'; Assert-True ([int64]$sale.changeCents -eq $expectedChangeCents) 'Second store sale change mismatch.'
Write-Step 'Create controlled sale from second store PASS'

Write-Step 'Validate sale detail, store filter and receipt...'
$saleDetail=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sales/$($sale.id)" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([string]$saleDetail.storeId -eq $storeId) 'Sale detail storeId mismatch.'; Assert-True ([string]$saleDetail.terminalId -eq $terminalId) 'Sale detail terminalId mismatch.'; Assert-True (@($saleDetail.lines).Count -ge 1) 'Sale detail has no lines.'; Assert-True (@($saleDetail.payments).Count -ge 1) 'Sale detail has no payments.'; Assert-True (@($saleDetail.inventoryMovements).Count -ge 1) 'Sale detail has no inventory movements.'
$saleOccurredAt=[DateTimeOffset]::Parse(([string]$saleDetail.occurredAt),[Globalization.CultureInfo]::InvariantCulture); $listedSale=Find-SaleInSalesReadModel -SaleId ([string]$sale.id) -StoreId $storeId -TerminalId $terminalId -Headers $adminHeaders -OccurredAt $saleOccurredAt; Assert-True ($null -ne $listedSale) 'Sales read model did not include second store sale.'
$mainStoreLeak=Invoke-DbScalar "select count(*) from pos.sales s join pos.stores st on st.tenant_id=s.tenant_id and st.id=s.store_id where s.tenant_id = '$TenantId' and s.id = '$($sale.id)' and st.code = 'MAIN';"; Assert-True ([int64]$mainStoreLeak -eq 0) 'Second store sale leaked into MAIN store.'
$receipt=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/receipts/$($sale.id)/issue" -Headers $terminalHeaders -ContentType 'application/json' -Body (@{}|ConvertTo-Json) -TimeoutSec 30
Assert-True ($receipt.status -eq 'active') 'Digital receipt was not active.'; Assert-True (-not [string]::IsNullOrWhiteSpace($receipt.publicToken)) 'Digital receipt public token missing.'
$protectedReceipt=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/$($sale.id)/digital" -Headers $adminHeaders -TimeoutSec 30; Assert-True ([string]$protectedReceipt.saleId -eq [string]$sale.id) 'Protected receipt saleId mismatch.'
Write-Step 'Validate sale detail, store filter and receipt PASS'

Write-Step 'Close second store cash shift...'
$summaryBeforeClose=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30; Assert-True ([int64]$summaryBeforeClose.cashSalesCents -ge $saleTotal) 'Shift summary did not include second store sale.'
$closedShift=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/close" -Headers $terminalHeaders -ContentType 'application/json' -Body (@{closedByUserId=$adminUserId;countedCashCents=$summaryBeforeClose.expectedCashCents}|ConvertTo-Json) -TimeoutSec 30
Assert-True ($closedShift.status -eq 'closed') 'Second store shift did not close.'
$summaryAfterClose=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $adminHeaders -TimeoutSec 30; Assert-True ($summaryAfterClose.status -eq 'closed') 'Closed shift summary did not return closed.'; Assert-True ([int64]$summaryAfterClose.differenceCents -eq 0) 'Second store shift difference must be zero.'
Write-Step 'Close second store cash shift PASS'

Write-Step 'Dashboard monitoring and audit evidence...'
$metricsAfter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?terminalId=$terminalId&limit=25" -Headers $adminHeaders -TimeoutSec 30; $pendingConflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&terminalId=$terminalId&limit=25" -Headers $adminHeaders -TimeoutSec 30; $auditEvents=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?entityId=$($sale.id)&limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($metricsAfter.database.ready -eq $true) 'Post-expansion metrics database.ready must be true.'; Assert-True ((Get-Items $pendingConflicts).Count -eq 0) 'Second store terminal has pending conflicts.'; Assert-True ((Get-Items $deadLetter).Count -eq 0) 'Second store terminal has dead-letter events.'
$saleAudit=Get-Items $auditEvents | Where-Object { $_.action -eq 'sale.completed' -and [string]$_.entityId -eq [string]$sale.id } | Select-Object -First 1; Assert-True ($null -ne $saleAudit) 'Audit read model did not include sale.completed for second store sale.'
Write-Step 'Dashboard monitoring and audit evidence PASS'

Write-Step 'SQL second store limited expansion cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id=$TenantId; store_id=$storeId; terminal_id=$terminalId; sale_id=$($sale.id); shift_id=$($shift.id); receipt_id=$($receipt.id); product_sku=$ProductSku; expected_total_cents=$saleTotal; expected_paid_cents=$paidCents; expected_change_cents=$expectedChangeCents; admin_user_id=$adminUserId }
if($sql.exp04SqlValidation -ne 'GO') { throw "EXP-04 SQL validation returned NO-GO. Reasons: $($sql.sqlBlockingReasons -join ',')" }
Write-Step 'SQL second store limited expansion cross-check PASS'

Write-Step 'Write second store expansion manifest and log...'
$conditions=@(); $retryPending=Get-LongValue -Object $metricsAfter.sync -Names @('retryPendingEvents') -Default ([long]$sql.retryPendingSyncCount); $deadLetterGlobal=Get-LongValue -Object $metricsAfter.sync -Names @('deadLetterEvents') -Default ([long]$sql.deadLetterSyncCount); $negativeInventory=Get-LongValue -Object $metricsAfter.inventory -Names @('negativeInventoryItemCount') -Default ([long]$sql.negativeInventoryItemCount)
if($retryPending -gt 0){$conditions += 'monitor_retry_pending_sync'}; if($deadLetterGlobal -gt 0){$conditions += 'triage_known_dead_letter'}; if($negativeInventory -gt 0){$conditions += 'inventory_reconciliation_required'}
$manifest=[ordered]@{ phase='EXP-04'; status='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'; tenantId=$TenantId; baseUrl=$script:base; generatedAt=(Get-Date).ToUniversalTime().ToString('o'); storeCode=$storeCode; storeId=$storeId; storeCountBefore=$storeCountBefore; storeCountAfter=$storeCountAfterCreate; terminalId=$terminalId; terminalFingerprint=$fingerprint; terminalCountBefore=$terminalCountBefore; terminalCountAfter=$terminalCountAfterRegister; shiftId=$shift.id; saleId=$sale.id; receiptId=$receipt.id; receiptNumber=$receipt.receiptNumber; productSku=$ProductSku; totalCents=$saleTotal; paidCents=$paidCents; changeCents=$expectedChangeCents; expectedCashCents=$summaryAfterClose.expectedCashCents; differenceCents=$summaryAfterClose.differenceCents; storeAccessCount=[long]$sql.storeAccessCount; storeSaleCount=[long]$sql.storeSaleCount; terminalDeadLetterCount=[long]$sql.terminalDeadLetterCount; terminalPendingConflictCount=[long]$sql.terminalPendingConflictCount; terminalSaleCount=[long]$sql.terminalSaleCount; schemaVersion=4; expansionDecision='GO_SECOND_STORE'; conditions=$conditions; nextPhase='EXP-05 Operational Monitoring Hardening' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path $logPath -Encoding UTF8 -Value '# SolidPOS EXP-04 Second Store Limited Expansion Log'; Add-Content -Path $logPath -Encoding UTF8 -Value ''; Add-Content -Path $logPath -Encoding UTF8 -Value "status: $($manifest.status)"; Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"; Add-Content -Path $logPath -Encoding UTF8 -Value "storeCode: $storeCode"; Add-Content -Path $logPath -Encoding UTF8 -Value "storeId: $storeId"; Add-Content -Path $logPath -Encoding UTF8 -Value "terminalId: $terminalId"; Add-Content -Path $logPath -Encoding UTF8 -Value "shiftId: $($shift.id)"; Add-Content -Path $logPath -Encoding UTF8 -Value "saleId: $($sale.id)"; Add-Content -Path $logPath -Encoding UTF8 -Value "receiptId: $($receipt.id)"; Add-Content -Path $logPath -Encoding UTF8 -Value "differenceCents: $($summaryAfterClose.differenceCents)"; Add-Content -Path $logPath -Encoding UTF8 -Value 'goNoGo: GO'
Write-Step 'Write second store expansion manifest and log PASS'
Write-Step 'EXP-04 PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
[pscustomobject]$manifest
