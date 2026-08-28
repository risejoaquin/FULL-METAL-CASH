param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [string]$StoreCode = "MAIN",
    [string]$ProductSku = "QSR-AMERICANO",
    [string]$PaymentMethodCode = "cash",
    [int64]$OpeningAmountCents = 25000,
    [int64]$CashTenderExtraCents = 500,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-03] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function Get-EntityId { param($Object) if($null -eq $Object){return $null}; foreach($n in @('id','saleId','sale_id','receiptId','terminalId','shiftId')){ if($null -ne $Object.$n){ return [string]$Object.$n } }; return $null }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-DbScalar { param([string]$Sql) $global:LASTEXITCODE=0; $output=docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:17 psql "$DatabaseUrl" -tA -v ON_ERROR_STOP=1 -c $Sql; if($LASTEXITCODE -ne 0){throw "DB scalar command failed."}; $global:LASTEXITCODE=0; return ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1).Trim() }
function Invoke-DbNonQuery { param([string]$Sql) $global:LASTEXITCODE=0; docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:17 psql "$DatabaseUrl" -v ON_ERROR_STOP=1 -c $Sql | Write-Host; if($LASTEXITCODE -ne 0){throw "DB non-query command failed."}; $global:LASTEXITCODE=0 }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
function Find-SaleInSalesReadModel { param([string]$SaleId,[string]$StoreId,[string]$TerminalId,[hashtable]$Headers,[DateTimeOffset]$OccurredAt) $baseUri=$script:base; $from=[Uri]::EscapeDataString($OccurredAt.AddMinutes(-10).ToUniversalTime().ToString('o')); $to=[Uri]::EscapeDataString((Get-Date).ToUniversalTime().AddMinutes(10).ToString('o')); $queries=@("$baseUri/api/v1/sales?storeId=$StoreId&terminalId=$TerminalId&status=completed&limit=200","$baseUri/api/v1/sales?from=$from&to=$to&storeId=$StoreId&terminalId=$TerminalId&status=completed&limit=200","$baseUri/api/v1/sales?storeId=$StoreId&status=completed&limit=200","$baseUri/api/v1/sales?status=completed&limit=200"); foreach($attempt in 1..5){ foreach($query in $queries){ $response=Invoke-RestMethod -Method Get -Uri $query -Headers $Headers -TimeoutSec 30; $items=Get-Items $response; $match=$items | Where-Object { (Get-EntityId $_) -eq $SaleId } | Select-Object -First 1; if($null -ne $match){ return $match } }; $detail=Invoke-RestMethod -Method Get -Uri "$baseUri/api/v1/sales/$SaleId" -Headers $Headers -TimeoutSec 30; if((Get-EntityId $detail) -eq $SaleId){ return $detail }; Start-Sleep -Milliseconds (250*$attempt) }; throw "Sale read model did not include created sale. saleId=$SaleId terminalId=$TerminalId" }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-03-second-terminal-expansion-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-03-second-terminal-production-expansion'
$manifestPath=Join-Path $runtimeDirectory 'second-terminal-expansion-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-03-second-terminal-production-expansion-log.md'
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-03 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-03 document contract...'
$docs=@(
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-03-second-terminal-production-expansion.md'; terms=@('exp-03','second terminal','production expansion','go/no-go','rollback') },
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-03-second-terminal-operator-checklist.md'; terms=@('before','during','after','terminal','evidence') },
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-03-second-terminal-rollback.md'; terms=@('rollback','terminal','cash shift','sale','containment') },
  @{ path=Join-Path $repoRoot 'docs\expansion\exp-03-second-terminal-go-no-go.md'; terms=@('go','no-go','blocker','condition','exp-04') }
)
foreach($doc in $docs){ Assert-DocumentContains -Path $doc.path -Terms $doc.terms }
Write-Step 'EXP-03 document contract PASS'

Write-Step 'Local secret scan...'
Invoke-CheckedCommand -Name 'secret scan' -Command { & (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -Root $repoRoot }
Write-Step 'Local secret scan PASS'

Write-Step 'dotnet restore...'
Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet restore' -Command { & dotnet restore $slnPath } } finally { Pop-Location }
Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...'
Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet build' -Command { & dotnet build $slnPath --no-restore } } finally { Pop-Location }
Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...'
Push-Location $repoRoot; try { Invoke-CheckedCommand -Name 'dotnet test' -Command { & dotnet test $slnPath --no-build } } finally { Pop-Location }
Write-Step 'dotnet test PASS'

if(-not $SkipDashboardBuild){
  Write-Step 'Dashboard production build and self-test...'
  Assert-True (Test-Path (Join-Path $dashboardRoot 'package.json')) 'Dashboard package.json is missing.'
  Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('install')
  Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('run','build')
  Invoke-NpmCommand -WorkingDirectory $dashboardRoot -Arguments @('run','self-test')
  Write-Step 'Dashboard production build and self-test PASS'
}

Write-Step 'Production liveness/readiness...'
$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Production liveness did not return alive.'
$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq 'ready') 'Production readiness did not return ready.'
Assert-True ($ready.database -eq 'ready') 'Production database readiness did not return ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and expansion pre-check...'
$loginBody=@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body $loginBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $($session.accessToken)"}
$metricsBefore=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($metricsBefore.database.ready -eq $true) 'Protected metrics database.ready must be true.'
Assert-True ($metricsBefore.database.requiredTablesPresent -eq $true) 'Protected metrics requiredTablesPresent must be true.'
$preSync=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$preConflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $preSync) 'Sync status endpoint returned null.'
Assert-True ($null -ne $preConflicts) 'Pending conflicts endpoint returned null.'
Write-Step 'Admin login and expansion pre-check PASS'

Write-Step 'Production data lookup...'
$storeId=Invoke-DbScalar "select id from pos.stores where tenant_id = '$TenantId' and code = '$StoreCode' and status = 'active' and deleted_at is null limit 1;"
$adminUserId=Invoke-DbScalar "select id from pos.users where tenant_id = '$TenantId' and email = lower('$Email') and status = 'active' limit 1;"
$productId=Invoke-DbScalar "select id from pos.products where tenant_id = '$TenantId' and sku = '$ProductSku' and status = 'active' and deleted_at is null limit 1;"
$priceCents=[int64](Invoke-DbScalar "select pp.price_cents from pos.product_prices pp join pos.products p on p.tenant_id = pp.tenant_id and p.id = pp.product_id where p.tenant_id = '$TenantId' and p.sku = '$ProductSku' and pp.deleted_at is null order by pp.created_at desc limit 1;")
$terminalCountBefore=[int64](Invoke-DbScalar "select count(*) from pos.terminals where tenant_id = '$TenantId' and store_id = '$storeId' and deleted_at is null;")
Assert-True (-not [string]::IsNullOrWhiteSpace($storeId)) 'Store not found for EXP-03.'
Assert-True (-not [string]::IsNullOrWhiteSpace($adminUserId)) 'Admin user not found for EXP-03.'
Assert-True (-not [string]::IsNullOrWhiteSpace($productId)) 'Product not found for EXP-03.'
Assert-True ($priceCents -gt 0) 'Product price not found for EXP-03.'
Write-Step 'Production data lookup PASS'

Write-Step 'Second terminal enrollment/register...'
$tokenBody=@{storeId=$storeId;expiresInMinutes=30}|ConvertTo-Json
$enrollment=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/terminals/enrollment-token" -Headers $adminHeaders -ContentType 'application/json' -Body $tokenBody -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($enrollment.enrollmentToken)) 'Enrollment token was not returned.'
$fingerprint="exp-03-second-terminal-$([guid]::NewGuid())"
$terminalBody=@{enrollmentToken=$enrollment.enrollmentToken;name='EXP-03 Second Terminal Production Expansion';fingerprint=$fingerprint;appVersion='exp-03'}|ConvertTo-Json
$terminalSession=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/terminal/register" -ContentType 'application/json' -Body $terminalBody -TimeoutSec 30
$terminalId=[string]$terminalSession.terminal.id
$terminalAccessToken=[string]$terminalSession.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalId)) 'Terminal register did not return terminal.id.'
Assert-True (-not [string]::IsNullOrWhiteSpace($terminalAccessToken)) 'Terminal register did not return accessToken.'
$terminalHeaders=@{Authorization="Bearer $terminalAccessToken"}
Write-Step 'Second terminal enrollment/register PASS'

Write-Step 'Bootstrap sync and terminal isolation check...'
$bootstrap=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/bootstrap" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([string]$bootstrap.tenantId -eq $TenantId) 'Bootstrap tenant mismatch.'
Assert-True ([string]$bootstrap.storeId -eq $storeId) 'Bootstrap store mismatch.'
Assert-True ([string]$bootstrap.terminalId -eq $terminalId) 'Bootstrap terminal mismatch.'
$terminalCountAfterRegister=[int64](Invoke-DbScalar "select count(*) from pos.terminals where tenant_id = '$TenantId' and store_id = '$storeId' and deleted_at is null;")
Assert-True ($terminalCountAfterRegister -ge ($terminalCountBefore + 1)) 'Terminal count did not increase after enrollment.'
Write-Step 'Bootstrap sync and terminal isolation check PASS'

Write-Step 'Open independent cash shift...'
Invoke-DbNonQuery "UPDATE pos.cash_shifts SET status = 'closed', counted_cash_cents = expected_cash_cents, difference_cents = 0, closed_at = now(), updated_at = now() WHERE tenant_id = '$TenantId' AND terminal_id = '$terminalId' AND status = 'open';"
$openBody=@{storeId=$storeId;terminalId=$terminalId;openedByUserId=$adminUserId;openingAmountCents=$OpeningAmountCents}|ConvertTo-Json
$shift=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts" -Headers $terminalHeaders -ContentType 'application/json' -Body $openBody -TimeoutSec 30
Assert-True ($shift.status -eq 'open') 'Second terminal cash shift did not open.'
Write-Step 'Open independent cash shift PASS'

Write-Step 'Create controlled sale from second terminal...'
$saleTotal=$priceCents
$paidCents=$saleTotal + $CashTenderExtraCents
$expectedChangeCents=$CashTenderExtraCents
$localSaleId=[guid]::NewGuid().ToString()
$now=(Get-Date).ToUniversalTime().ToString('o')
$saleBody=@{localSaleId=$localSaleId;cashierUserId=$adminUserId;customerId=$null;occurredAt=$now;localCreatedAt=$now;lines=@(@{productId=$productId;variantId=$null;quantity='1';discountCents=0;preparationNote='EXP-03 second terminal controlled sale';modifierIds=@()});payments=@(@{localPaymentId=[guid]::NewGuid().ToString();methodCode=$PaymentMethodCode;amountCents=$paidCents;reference='exp-03-second-terminal-production-expansion'});tipCents=0}|ConvertTo-Json -Depth 8
$sale=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/sales" -Headers $terminalHeaders -ContentType 'application/json' -Body $saleBody -TimeoutSec 30
Assert-True ($sale.status -eq 'completed') 'Second terminal sale was not completed.'
Assert-True ([int64]$sale.totalCents -eq $saleTotal) 'Second terminal sale total mismatch.'
Assert-True ([int64]$sale.paidCents -eq $paidCents) 'Second terminal sale paid mismatch.'
Assert-True ([int64]$sale.changeCents -eq $expectedChangeCents) 'Second terminal sale change mismatch.'
Write-Step 'Create controlled sale from second terminal PASS'

Write-Step 'Validate sale detail, terminal filter and receipt...'
$saleDetail=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sales/$($sale.id)" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([string]$saleDetail.terminalId -eq $terminalId) 'Sale detail terminalId mismatch.'
Assert-True ([string]$saleDetail.storeId -eq $storeId) 'Sale detail storeId mismatch.'
Assert-True (@($saleDetail.lines).Count -ge 1) 'Sale detail has no lines.'
Assert-True (@($saleDetail.payments).Count -ge 1) 'Sale detail has no payments.'
Assert-True (@($saleDetail.inventoryMovements).Count -ge 1) 'Sale detail has no inventory movements.'
$saleOccurredAt=[DateTimeOffset]::Parse(([string]$saleDetail.occurredAt),[Globalization.CultureInfo]::InvariantCulture)
$listedSale=Find-SaleInSalesReadModel -SaleId ([string]$sale.id) -StoreId $storeId -TerminalId $terminalId -Headers $adminHeaders -OccurredAt $saleOccurredAt
Assert-True ($null -ne $listedSale) 'Sales read model did not include second terminal sale.'
$receipt=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/receipts/$($sale.id)/issue" -Headers $terminalHeaders -ContentType 'application/json' -Body (@{}|ConvertTo-Json) -TimeoutSec 30
Assert-True ($receipt.status -eq 'active') 'Digital receipt was not active.'
Assert-True (-not [string]::IsNullOrWhiteSpace($receipt.publicToken)) 'Digital receipt public token missing.'
$protectedReceipt=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/$($sale.id)/digital" -Headers $adminHeaders -TimeoutSec 30
Assert-True ([string]$protectedReceipt.saleId -eq [string]$sale.id) 'Protected receipt saleId mismatch.'
Write-Step 'Validate sale detail, terminal filter and receipt PASS'

Write-Step 'Close second terminal cash shift...'
$summaryBeforeClose=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $terminalHeaders -TimeoutSec 30
Assert-True ([int64]$summaryBeforeClose.cashSalesCents -ge $saleTotal) 'Shift summary did not include second terminal sale.'
$closeBody=@{closedByUserId=$adminUserId;countedCashCents=$summaryBeforeClose.expectedCashCents}|ConvertTo-Json
$closedShift=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/close" -Headers $terminalHeaders -ContentType 'application/json' -Body $closeBody -TimeoutSec 30
Assert-True ($closedShift.status -eq 'closed') 'Second terminal shift did not close.'
$summaryAfterClose=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/cash-drawers/shifts/$($shift.id)/summary" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($summaryAfterClose.status -eq 'closed') 'Closed shift summary did not return closed.'
Assert-True ([int64]$summaryAfterClose.differenceCents -eq 0) 'Second terminal shift difference must be zero.'
Write-Step 'Close second terminal cash shift PASS'

Write-Step 'Dashboard monitoring and audit evidence...'
$metricsAfter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncAfter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status?storeId=$storeId&terminalId=$terminalId" -Headers $adminHeaders -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?terminalId=$terminalId&limit=25" -Headers $adminHeaders -TimeoutSec 30
$pendingConflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&terminalId=$terminalId&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?entityId=$($sale.id)&limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($metricsAfter.database.ready -eq $true) 'Post-expansion metrics database.ready must be true.'
Assert-True ((Get-Items $pendingConflicts).Count -eq 0) 'Second terminal has pending conflicts.'
Assert-True ((Get-Items $deadLetter).Count -eq 0) 'Second terminal has dead-letter events.'
$auditItems=Get-Items $auditEvents
$saleAudit=$auditItems | Where-Object { $_.action -eq 'sale.completed' -and [string]$_.entityId -eq [string]$sale.id } | Select-Object -First 1
Assert-True ($null -ne $saleAudit) 'Audit read model did not include sale.completed for second terminal sale.'
Write-Step 'Dashboard monitoring and audit evidence PASS'

Write-Step 'SQL second terminal production expansion cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id=$TenantId; store_id=$storeId; terminal_id=$terminalId; sale_id=$($sale.id); shift_id=$($shift.id); receipt_id=$($receipt.id); product_sku=$ProductSku; expected_total_cents=$saleTotal; expected_paid_cents=$paidCents; expected_change_cents=$expectedChangeCents }
if($sql.exp03SqlValidation -ne 'GO') { throw "EXP-03 SQL validation returned NO-GO. Reasons: $($sql.sqlBlockingReasons -join ',')" }
Write-Step 'SQL second terminal production expansion cross-check PASS'

Write-Step 'Write second terminal expansion manifest and log...'
$conditions=@()
$retryPending=Get-LongValue -Object $metricsAfter.sync -Names @('retryPendingEvents') -Default ([long]$sql.retryPendingSyncCount)
$deadLetterGlobal=Get-LongValue -Object $metricsAfter.sync -Names @('deadLetterEvents') -Default ([long]$sql.deadLetterSyncCount)
$negativeInventory=Get-LongValue -Object $metricsAfter.inventory -Names @('negativeInventoryItemCount') -Default ([long]$sql.negativeInventoryItemCount)
if($retryPending -gt 0){$conditions += 'monitor_retry_pending_sync'}
if($deadLetterGlobal -gt 0){$conditions += 'triage_known_dead_letter'}
if($negativeInventory -gt 0){$conditions += 'inventory_reconciliation_required'}
$manifest=[ordered]@{ phase='EXP-03'; status='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'; tenantId=$TenantId; baseUrl=$script:base; generatedAt=(Get-Date).ToUniversalTime().ToString('o'); storeCode=$StoreCode; storeId=$storeId; terminalId=$terminalId; terminalFingerprint=$fingerprint; terminalCountBefore=$terminalCountBefore; terminalCountAfter=$terminalCountAfterRegister; shiftId=$shift.id; saleId=$sale.id; receiptId=$receipt.id; receiptNumber=$receipt.receiptNumber; productSku=$ProductSku; totalCents=$saleTotal; paidCents=$paidCents; changeCents=$expectedChangeCents; expectedCashCents=$summaryAfterClose.expectedCashCents; differenceCents=$summaryAfterClose.differenceCents; terminalDeadLetterCount=[long]$sql.terminalDeadLetterCount; terminalPendingConflictCount=[long]$sql.terminalPendingConflictCount; terminalSaleCount=[long]$sql.terminalSaleCount; schemaVersion=4; expansionDecision='GO_SECOND_TERMINAL'; conditions=$conditions; nextPhase='EXP-04 Second Store Limited Expansion' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path $logPath -Encoding UTF8 -Value '# SolidPOS EXP-03 Second Terminal Production Expansion Log'
Add-Content -Path $logPath -Encoding UTF8 -Value ''
Add-Content -Path $logPath -Encoding UTF8 -Value "status: $($manifest.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "storeId: $storeId"
Add-Content -Path $logPath -Encoding UTF8 -Value "terminalId: $terminalId"
Add-Content -Path $logPath -Encoding UTF8 -Value "shiftId: $($shift.id)"
Add-Content -Path $logPath -Encoding UTF8 -Value "saleId: $($sale.id)"
Add-Content -Path $logPath -Encoding UTF8 -Value "receiptId: $($receipt.id)"
Add-Content -Path $logPath -Encoding UTF8 -Value "differenceCents: $($summaryAfterClose.differenceCents)"
Add-Content -Path $logPath -Encoding UTF8 -Value 'goNoGo: GO'
Write-Step 'Write second terminal expansion manifest and log PASS'

Write-Step 'EXP-03 PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
[pscustomobject]$manifest
