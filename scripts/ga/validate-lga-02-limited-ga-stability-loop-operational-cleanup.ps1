param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('FORMAL_ARCHIVE','REMEDIATED')][string]$ConflictDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('FORMAL_ARCHIVE','RETRY_OR_ARCHIVE')][string]$DeadLetterDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('ADJUSTED','RECONCILED','OBSERVE')][string]$InventoryDecision = 'ADJUSTED',
    [int]$MaxStores = 2,
    [int]$MaxConcurrentTerminals = 2,
    [int]$AllowedExistingSyncConflictCount = 3,
    [int]$AllowedDeadLetterCount = 1,
    [int]$AllowedNegativeStockItemCount = 0,
    [int]$AllowedWaitingConnectionCount = 11,
    [int]$BaselineCompletedSalesInLast24h = 3,
    [int]$BaselinePaymentsInLast24h = 3,
    [int]$MinimumNewCompletedSales = 3,
    [int]$MinimumNewPayments = 3,
    [switch]$ApplyInventoryAdjustment,
    [switch]$WpfVisualConfirmed,
    [switch]$SkipDashboardBuild,
    [switch]$SkipLga01Revalidation
)
$ErrorActionPreference = 'Stop'
$script:Lga02ValidatorVersion = 'LGA-02.3-inventory-adjustment-contract-alignment'
function Write-Step([string]$m){ Write-Host "[LGA-02] $m" }
function Assert-True($c,[string]$m){ if(-not $c){ throw $m } }
function Convert-Secret([securestring]$s){ $b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s); try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)} finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)} }
function Invoke-Checked([string]$n,[scriptblock]$c){ $global:LASTEXITCODE=0; & $c; $e=$LASTEXITCODE; $global:LASTEXITCODE=0; if($e -ne 0){throw "$n failed with exit code $e"} }
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{},[int]$TimeoutSec=30){
  $p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=$TimeoutSec}
  if($null -ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 80;$p.ContentType='application/json'}
  try{return Invoke-RestMethod @p}catch{ $status='';$text=''; if($_.Exception.Response){try{$status="; httpStatus=$([int]$_.Exception.Response.StatusCode)"}catch{};try{$r=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream());$text=$r.ReadToEnd();$r.Close()}catch{}}; if($text){$status="$status; response=$text"}; throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"}
}
function Get-HttpStatus([string]$Method,[string]$UriOrPath,[hashtable]$Headers=@{},[switch]$Absolute){
  $u=if($Absolute.IsPresent){$UriOrPath}else{"$script:base$UriOrPath"}
  try{$r=Invoke-WebRequest -Method $Method -Uri $u -Headers $Headers -TimeoutSec 30 -UseBasicParsing; return [int]$r.StatusCode}catch{ if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode}; throw }
}
function Req($o,[string]$n){ Assert-True ($null -ne $o) "Object missing while checking property $n"; $p=$o.PSObject.Properties[$n]; Assert-True ($null -ne $p) "Required property missing: $n"; return $p.Value }
function Optional-Prop($o,[string]$n){ if($null -eq $o){ return $null }; $p=$o.PSObject.Properties[$n]; if($null -eq $p){ return $null }; return $p.Value }
function Assert-DocumentContains([string]$Path,[string[]]$Terms){ Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Assert-2xx([int]$s,[string]$n){Assert-True (($s -ge 200 -and $s -lt 300)) "$n must return 2xx; status=$s"}
function Invoke-DbJsonFile([string]$SqlPath,[hashtable]$Vars){
  $dir=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $file=Split-Path -Leaf $SqlPath
  $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${dir}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
  foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")}; $args += @('-f',"/sql/$file")
  $global:LASTEXITCODE=0; $out=docker @args; $e=$LASTEXITCODE; $global:LASTEXITCODE=0; if($e -ne 0){throw "Database SQL failed: $file"}
  $raw=($out|ForEach-Object{[string]$_}) -join "`n"; $idx=$raw.LastIndexOf('LGA02_JSON:'); if($idx -ge 0){$json=$raw.Substring($idx+'LGA02_JSON:'.Length).Trim(); return $json|ConvertFrom-Json}
  foreach($line in $out){$str=([string]$line).Trim(); if($str.StartsWith('{')){try{return $str|ConvertFrom-Json}catch{}}}; throw "No JSON result returned by $file. Raw=$raw"
}

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-Secret $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln=Join-Path $repo 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'lga-02-limited-ga-stability-loop-operational-cleanup-check.sql'
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript=Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtimeDir=Join-Path $repo '.runtime\lga-02-limited-ga-stability-loop-operational-cleanup'
$wpfSalesVm=Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\ViewModels\SalesViewModel.cs'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:Lga02ValidatorVersion"
Write-Step 'Repository/document LGA-02 guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing'
Assert-True (Test-Path $sqlPath) 'LGA-02 SQL check missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-02-limited-ga-stability-loop-operational-cleanup.md') @('lga-02','limited ga','stability loop','public ga not activated')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-02-real-sales-cycle-record.md') @('real sales','completed sales','payments','stability loop')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-02-sync-dead-letter-stability-record.md') @('sync conflicts','dead letter','baseline','must not increase')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-02-inventory-reconciliation-record.md') @('ing-cafe-g','negative stock','reconcile','inventory adjustment')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-02-wpf-qsr-visual-confirmation.md') @('wpf','qsr','visual confirmation','cash payment')
Write-Step 'Repository/document LGA-02 guardrails PASS'

Write-Step 'Local build/test/secret/WPF guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln }
Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore }
Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Assert-True (Test-Path $secretScan) 'Secret scan script missing'
Invoke-Checked 'secret scan' { & $secretScan -Root $repo }
Assert-True (Test-Path $wpfSalesVm) 'SalesViewModel missing'
$wpfSource=Get-Content -Raw $wpfSalesVm
Assert-True ($wpfSource.Contains('RefreshCommandStates')) 'WPF QSR command refresh helper missing'
Assert-True ($wpfSource.Contains('RaiseCanExecuteChanged')) 'WPF QSR RaiseCanExecuteChanged calls missing'
Assert-True ($wpfSource.Contains('public RelayCommand TakeCashPaymentCommand')) 'WPF TakeCashPaymentCommand must expose RelayCommand for refresh'
Assert-True ($WpfVisualConfirmed.IsPresent) 'WPF visual confirmation missing. Open PosCore WPF, confirm Cobrar efectivo / Encolar recibo fake / Actualizar sync visual enable correctly, then rerun with -WpfVisualConfirmed.'
if(-not $SkipDashboardBuild.IsPresent){Assert-True (Test-Path $dashboardScript) 'PosDashboard validation script missing'; Invoke-Checked 'PosDashboard validation' { & $dashboardScript -SkipBuild:$false }}else{Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret/WPF guardrails PASS'

if($SkipLga01Revalidation.IsPresent){Write-Step 'LGA-01 prerequisite revalidation skipped by switch; LGA-02 requires existing LGA-01 PASS logs or runtime manifest.'}

Write-Step 'Limited GA stability loop API checks...'
$login=Invoke-Api 'POST' '/api/v1/auth/login' @{email=$Email;password=$plainPassword;tenantId=$TenantId}
$accessToken=Req $login 'accessToken'; $user=Req $login 'user'; $createdByUserId=[string](Req $user 'id'); $headers=@{Authorization="Bearer $accessToken"}
$healthLiveStatus=Get-HttpStatus 'GET' '/health/live'; $healthReadyStatus=Get-HttpStatus 'GET' '/health/ready'; Assert-2xx $healthLiveStatus 'health/live'; Assert-2xx $healthReadyStatus 'health/ready'
$unauthObservabilityStatus=Get-HttpStatus 'GET' '/api/v1/observability/metrics'; Assert-True ($unauthObservabilityStatus -eq 401) "unauthenticated observability must return 401; status=$unauthObservabilityStatus"
$metrics=Invoke-Api 'GET' '/api/v1/observability/metrics' $null $headers; $metricsDatabaseReady=[bool](Req (Req $metrics 'database') 'ready'); Assert-True $metricsDatabaseReady 'metrics database.ready must be true'
$sync=Invoke-Api 'GET' '/api/v1/sync/status' $null $headers
$syncPendingCount=[int](Req $sync 'pendingCount'); $syncProcessingCount=[int](Req $sync 'processingCount'); $syncRetryPendingCount=[int](Req $sync 'retryPendingCount'); $syncConflictCount=[int](Req $sync 'conflictCount'); $syncDeadLetterCount=[int](Req $sync 'deadLetterCount')
Assert-True ($syncPendingCount -eq 0) "sync pendingCount must be 0; actual=$syncPendingCount"; Assert-True ($syncProcessingCount -eq 0) "sync processingCount must be 0; actual=$syncProcessingCount"; Assert-True ($syncRetryPendingCount -eq 0) "sync retryPendingCount must be 0; actual=$syncRetryPendingCount"
$contract=Invoke-Api 'GET' '/api/v1/sync/contract' $null $headers; $contractSchema=Optional-Prop $contract 'currentSchemaVersion'; if($null -eq $contractSchema){$contractSchema=Optional-Prop $contract 'schemaVersion'}; Assert-True ([int]$contractSchema -eq 4) "sync contract schema version must be 4; actual=$contractSchema"
$from=[uri]::EscapeDataString((Get-Date).ToUniversalTime().AddHours(-24).ToString('o')); $to=[uri]::EscapeDataString((Get-Date).ToUniversalTime().ToString('o'))
$salesRange=Invoke-Api 'GET' "/api/v1/reports/sales/range?from=$from&to=$to" $null $headers
$dashboardOverview=Invoke-Api 'GET' "/api/v1/reports/dashboard/overview?from=$from&to=$to&limit=20&trendBucket=day" $null $headers
$inventoryStatus=Get-HttpStatus 'GET' '/api/v1/inventory/stock' $headers; Assert-2xx $inventoryStatus 'inventory stock'
$dashboardUrlStatus=0; if(-not [string]::IsNullOrWhiteSpace($DashboardUrl)){ $dashboardUrlStatus=Get-HttpStatus 'GET' $DashboardUrl @{} -Absolute; Assert-True ($dashboardUrlStatus -ge 200 -and $dashboardUrlStatus -lt 500) "dashboard URL must be reachable; status=$dashboardUrlStatus" }
Write-Step 'Limited GA stability loop API checks PASS'

Write-Step 'Database Limited GA stability snapshot...'
$dbVars=@{tenant_id=$TenantId; max_stores=$MaxStores; max_concurrent_terminals=$MaxConcurrentTerminals; allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount; allowed_dead_letters=$AllowedDeadLetterCount; allowed_waiting_connections=$AllowedWaitingConnectionCount}
$db=Invoke-DbJsonFile $sqlPath $dbVars
Write-Step 'Database Limited GA stability snapshot PASS'

Write-Step 'Inventory reconciliation gate...'
$negativeStock=Req $db 'negativeStock'; $negativeStockCount=[int](Req $negativeStock 'count')
$inventoryAdjustmentApplied=$false
if($ApplyInventoryAdjustment.IsPresent -and $negativeStockCount -gt 0){
  $items=@(Req $negativeStock 'items')
  foreach($item in $items){
    $adjustmentQty=[decimal](Req $item 'adjustment_quantity_needed')
    $quantityDelta=$adjustmentQty.ToString([Globalization.CultureInfo]::InvariantCulture)
    $body=@{localAdjustmentId=[guid]::NewGuid();storeId=[string](Req $item 'store_id');adjustmentType='correction';reason='LGA-02 controlled ING-CAFE-G inventory reconciliation';createdByUserId=$createdByUserId;occurredAt=(Get-Date).ToUniversalTime().ToString('o');lines=@(@{productId=[string](Req $item 'product_id');variantId=(Optional-Prop $item 'variant_id');quantityDelta=$quantityDelta;unitId=[string](Req $item 'unit_id');costCents=$null})}
    $null=Invoke-Api 'POST' '/api/v1/inventory/adjustments' $body $headers
    $inventoryAdjustmentApplied=$true
  }
  $db=Invoke-DbJsonFile $sqlPath $dbVars
  $negativeStock=Req $db 'negativeStock'; $negativeStockCount=[int](Req $negativeStock 'count')
}
if($InventoryDecision -in @('ADJUSTED','RECONCILED')){ Assert-True ($ApplyInventoryAdjustment.IsPresent -or $negativeStockCount -eq 0) 'InventoryDecision ADJUSTED/RECONCILED requires -ApplyInventoryAdjustment or already-zero negative stock.'; Assert-True ($negativeStockCount -le $AllowedNegativeStockItemCount) "negative stock must be within allowed post-reconciliation baseline; actual=$negativeStockCount allowed=$AllowedNegativeStockItemCount" }
Write-Step 'Inventory reconciliation gate PASS'

Write-Step 'LGA-02 blocker matrix...'
$blockers=[ordered]@{}; $conditions=New-Object System.Collections.Generic.List[string]
function Add-Blocker([string]$k,$v){$script:blockers[$k]=$v}; function Add-Condition([string]$v){[void]$conditions.Add($v)}
$tenantState=Req $db 'tenantState'; $rolloutScope=Req $db 'rolloutScope'; $syncIntegrity=Req $db 'syncIntegrity'; $financialIntegrity=Req $db 'financialIntegrity'; $dbPressure=Req $db 'databasePressure'; $rls=Req $db 'rls'; $monitoringActivity=Req $db 'monitoringActivity'
$activeStoreCount=[int64](Req $rolloutScope 'active_store_count'); $openShiftCount=[int64](Req $rolloutScope 'open_shift_count'); $activeStableReleaseCount=[int64](Req $rolloutScope 'active_stable_release_count'); $availableTerminalCount=[int64](Req $rolloutScope 'available_terminal_count')
$legacySchemaEventCount=[int64](Req $syncIntegrity 'legacy_schema_event_count'); $pendingConflictCount=[int64](Req $syncIntegrity 'pending_conflict_count'); $retryPendingCount=[int64](Req $syncIntegrity 'retry_pending_count'); $deadLetterCount=[int64](Req $syncIntegrity 'dead_letter_count'); $staleProcessingCount=[int64](Req $syncIntegrity 'stale_processing_count')
$duplicateLocalSaleCount=[int64](Req $financialIntegrity 'duplicate_local_sale_count'); $negativePaymentCount=[int64](Req $financialIntegrity 'negative_payment_count'); $waitingConnectionCount=[int64](Req $dbPressure 'waiting_connection_count'); $longRunningQueryCount=[int64](Req $dbPressure 'long_running_query_count'); $rlsMissingTableCount=[int64](Req $rls 'rls_missing_table_count')
$completedSales24h=[int64](Req $monitoringActivity 'completed_sales_24h'); $payments24h=[int64](Req $monitoringActivity 'payments_24h'); $receiptsIssued24h=[int64](Req $monitoringActivity 'receipts_issued_24h')
$gaActivated=[bool](Req $db 'generalAvailabilityActivated'); $publicGaActivated=[bool](Req $db 'publicGeneralAvailabilityActivated')
$requiredCompletedSales=$BaselineCompletedSalesInLast24h + $MinimumNewCompletedSales
$requiredPayments=$BaselinePaymentsInLast24h + $MinimumNewPayments
if(-not [bool](Req $db 'requiredTablesPresent')){Add-Blocker 'missing_required_tables' (Req $db 'missingRequiredTables')}; if([int64](Req $tenantState 'active_tenant_count') -ne 1){Add-Blocker 'tenant_not_active' $tenantState}
if($activeStoreCount -lt 1){Add-Blocker 'no_active_store' $activeStoreCount}; if($activeStoreCount -gt $MaxStores){Add-Blocker 'active_store_count_exceeds_limited_scope' @{actual=$activeStoreCount;max=$MaxStores}}
if($openShiftCount -gt $MaxConcurrentTerminals){Add-Blocker 'open_shift_count_exceeds_terminal_limit' @{actual=$openShiftCount;max=$MaxConcurrentTerminals}}; if($activeStableReleaseCount -lt 1){Add-Blocker 'no_active_stable_release' $activeStableReleaseCount}
if($legacySchemaEventCount -ne 0){Add-Blocker 'legacy_schema_events' $legacySchemaEventCount}; if($retryPendingCount -ne 0){Add-Blocker 'retry_pending_sync_events' $retryPendingCount}; if($staleProcessingCount -ne 0){Add-Blocker 'stale_processing_sync_events' $staleProcessingCount}; if($duplicateLocalSaleCount -ne 0){Add-Blocker 'duplicate_local_sales' $duplicateLocalSaleCount}; if($negativePaymentCount -ne 0){Add-Blocker 'negative_payments' $negativePaymentCount}; if($rlsMissingTableCount -ne 0){Add-Blocker 'rls_missing_tenant_tables' $rlsMissingTableCount}; if($longRunningQueryCount -ne 0){Add-Blocker 'long_running_queries' $longRunningQueryCount}
if($pendingConflictCount -gt $AllowedExistingSyncConflictCount){Add-Blocker 'pending_conflicts_increased_above_baseline' @{actual=$pendingConflictCount;allowed=$AllowedExistingSyncConflictCount}}; if($deadLetterCount -gt $AllowedDeadLetterCount){Add-Blocker 'dead_letters_increased_above_baseline' @{actual=$deadLetterCount;allowed=$AllowedDeadLetterCount}}; if($negativeStockCount -gt $AllowedNegativeStockItemCount){Add-Blocker 'negative_stock_not_reconciled' @{actual=$negativeStockCount;allowed=$AllowedNegativeStockItemCount}}; if($waitingConnectionCount -gt $AllowedWaitingConnectionCount){Add-Blocker 'waiting_connections_exceed_allowed_baseline' @{actual=$waitingConnectionCount;allowed=$AllowedWaitingConnectionCount}}
if($completedSales24h -lt $requiredCompletedSales){Add-Blocker 'new_sales_cycle_not_observed' @{completedSales24h=$completedSales24h;baseline=$BaselineCompletedSalesInLast24h;minimumNew=$MinimumNewCompletedSales;required=$requiredCompletedSales}}
if($payments24h -lt $requiredPayments){Add-Blocker 'new_payment_cycle_not_observed' @{payments24h=$payments24h;baseline=$BaselinePaymentsInLast24h;minimumNew=$MinimumNewPayments;required=$requiredPayments}}
if($ConflictDecision -eq 'REMEDIATED' -and $pendingConflictCount -ne 0){Add-Blocker 'conflict_decision_remediated_requires_zero_pending_conflicts' $pendingConflictCount}
if($DeadLetterDecision -eq 'RETRY_OR_ARCHIVE' -and $deadLetterCount -gt $AllowedDeadLetterCount){Add-Blocker 'dead_letter_retry_or_archive_exceeds_baseline' $deadLetterCount}
if($gaActivated){Add-Blocker 'general_availability_flag_active' $true}; if($publicGaActivated){Add-Blocker 'public_general_availability_flag_active' $true}
Add-Condition 'public_ga_not_activated_by_lga02'; Add-Condition 'limited_ga_stability_loop_only'; Add-Condition 'wpf_qsr_visual_confirmation_required_and_provided'; Add-Condition 'new_real_sales_cycle_required'; Add-Condition 'sync_conflict_count_must_not_increase_above_baseline'; Add-Condition 'dead_letter_count_must_not_increase_above_baseline'; Add-Condition 'inventory_reconciliation_gate_enforced'; Add-Condition 'ga09_capacity_boundary_concurrency_3_plus_carried_forward'
if($inventoryAdjustmentApplied){Add-Condition 'inventory_adjustment_applied_by_lga02'; Add-Condition 'inventory_adjustment_contract_aligned_to_correction_type'}; if($waitingConnectionCount -gt 0){Add-Condition 'db_waiting_connections_observation_carried_forward'}
Assert-True ($blockers.Count -eq 0) ("LGA-02 blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
Write-Step 'LGA-02 blocker matrix PASS'
$status='PASS LGA-02 LIMITED GA STABILITY LOOP AND OPERATIONAL CLEANUP / GO LGA-03'
$manifest=[ordered]@{phase='LGA-02';status=$status;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');validatorVersion=$script:Lga02ValidatorVersion;entryGate='PASS LGA-01 LIMITED GA OPERATIONS HARDENING / GO LGA-02';conflictDecision=$ConflictDecision;deadLetterDecision=$DeadLetterDecision;inventoryDecision=$InventoryDecision;maxStores=$MaxStores;maxConcurrentTerminals=$MaxConcurrentTerminals;baselineCompletedSalesInLast24h=$BaselineCompletedSalesInLast24h;minimumNewCompletedSales=$MinimumNewCompletedSales;baselinePaymentsInLast24h=$BaselinePaymentsInLast24h;minimumNewPayments=$MinimumNewPayments;requiredCompletedSalesInLast24h=$requiredCompletedSales;requiredPaymentsInLast24h=$requiredPayments;allowedExistingSyncConflictCount=$AllowedExistingSyncConflictCount;allowedDeadLetterCount=$AllowedDeadLetterCount;allowedNegativeStockItemCount=$AllowedNegativeStockItemCount;allowedWaitingConnectionCount=$AllowedWaitingConnectionCount;publicGeneralAvailabilityActivated=$false;generalAvailabilityActivated=$false;publicGaActivatedByValidator=$false;healthLiveStatus=$healthLiveStatus;healthReadyStatus=$healthReadyStatus;unauthenticatedObservabilityStatus=$unauthObservabilityStatus;inventoryStockStatus=$inventoryStatus;salesRangeCompletedSalesCount=[int](Req $salesRange 'completedSalesCount');dashboardOverviewCompletedSalesCount=[int](Req (Req $dashboardOverview 'sales') 'completedSalesCount');completedSalesInLast24h=$completedSales24h;paymentsInLast24h=$payments24h;receiptsIssuedInLast24h=$receiptsIssued24h;syncPendingCount=$syncPendingCount;syncProcessingCount=$syncProcessingCount;syncRetryPendingCount=$syncRetryPendingCount;syncConflictCount=$syncConflictCount;syncDeadLetterCount=$syncDeadLetterCount;dashboardUrlStatus=$dashboardUrlStatus;dashboardBuild=if($SkipDashboardBuild.IsPresent){'SKIPPED_BY_SWITCH'}else{'VALIDATED'};metricsDatabaseReady=$metricsDatabaseReady;syncContractCurrentSchemaVersion=[int]$contractSchema;databaseSnapshot=$db;negativeStockCount=$negativeStockCount;negativeStock=$negativeStock;inventoryAdjustmentApplied=$inventoryAdjustmentApplied;activeStoreCount=$activeStoreCount;availableTerminalCount=$availableTerminalCount;openShiftCount=$openShiftCount;activeStableReleaseCount=$activeStableReleaseCount;duplicateLocalSaleCount=$duplicateLocalSaleCount;legacySchemaEventCount=$legacySchemaEventCount;pendingConflictCount=$pendingConflictCount;retryPendingCount=$retryPendingCount;deadLetterCount=$deadLetterCount;staleProcessingCount=$staleProcessingCount;rlsMissingTableCount=$rlsMissingTableCount;waitingConnectionCount=$waitingConnectionCount;longRunningQueryCount=$longRunningQueryCount;wpfQsrVisualConfirmed=$WpfVisualConfirmed.IsPresent;blockers=$blockers;conditions=@($conditions);schemaVersion=4;syncContract='schema_version_4';publicGaActivation='NOT_ACTIVATED';nextPhase='LGA-03 - Limited GA Multi-Day Stability Burn-In'}
$manifestPath=Join-Path $runtimeDir 'lga-02-limited-ga-stability-loop-operational-cleanup-manifest.json'; $manifest|ConvertTo-Json -Depth 80|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'LGA-02 evidence manifest and stability loop snapshot PASS'
Write-Step $status
$manifest|Format-List
