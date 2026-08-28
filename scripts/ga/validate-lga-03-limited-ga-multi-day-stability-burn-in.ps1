param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('FORMAL_ARCHIVE','REMEDIATED')][string]$ConflictDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('FORMAL_ARCHIVE','RETRY_OR_ARCHIVE')][string]$DeadLetterDecision = 'FORMAL_ARCHIVE',
    [int]$BurnInCheckpoint = 1,
    [int]$RequiredBurnInDays = 3,
    [int]$MaxStores = 2,
    [int]$MaxConcurrentTerminals = 2,
    [int]$MinCompletedSalesInLast24h = 6,
    [int]$MinPaymentsInLast24h = 6,
    [int]$MinReceiptsIssuedInLast24h = 3,
    [int]$AllowedExistingSyncConflictCount = 3,
    [int]$AllowedDeadLetterCount = 1,
    [int]$AllowedNegativeStockItemCount = 0,
    [int]$AllowedOpenShiftCount = 0,
    [int]$AllowedWaitingConnectionCount = 11,
    [switch]$FinalizeBurnIn,
    [switch]$WpfVisualConfirmed,
    [switch]$SkipDashboardBuild,
    [switch]$SkipLga02Revalidation
)
$ErrorActionPreference='Stop'
$script:Lga03ValidatorVersion='LGA-03.2-dashboard-overview-endpoint-contract-alignment'
function Write-Step([string]$m){Write-Host "[LGA-03] $m"}
function Assert-True($c,[string]$m){if(-not $c){throw $m}}
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;& $c;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "$n failed with exit code $e"}}
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{},[int]$TimeoutSec=30){$p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=$TimeoutSec};if($null -ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 80;$p.ContentType='application/json'};try{return Invoke-RestMethod @p}catch{$status='';$text='';if($_.Exception.Response){try{$status="; httpStatus=$([int]$_.Exception.Response.StatusCode)"}catch{};try{$r=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream());$text=$r.ReadToEnd();$r.Close()}catch{}};if($text){$status="$status; response=$text"};throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"}}
function Get-HttpStatus([string]$Method,[string]$UriOrPath,[hashtable]$Headers=@{},[switch]$Absolute){$u=if($Absolute.IsPresent){$UriOrPath}else{"$script:base$UriOrPath"};try{$r=Invoke-WebRequest -Method $Method -Uri $u -Headers $Headers -TimeoutSec 30 -UseBasicParsing;return [int]$r.StatusCode}catch{if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
function Req($o,[string]$n){Assert-True ($null -ne $o) "Object missing while checking property $n";$p=$o.PSObject.Properties[$n];Assert-True ($null -ne $p) "Required property missing: $n";return $p.Value}
function Optional-Prop($o,[string]$n){if($null -eq $o){return $null};$p=$o.PSObject.Properties[$n];if($null -eq $p){return $null};return $p.Value}
function Assert-DocumentContains([string]$Path,[string[]]$Terms){Assert-True (Test-Path $Path) "Required document missing: $Path";$c=(Get-Content -Raw $Path).ToLowerInvariant();foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"}}
function Assert-2xx([int]$s,[string]$n){Assert-True (($s -ge 200 -and $s -lt 300)) "$n must return 2xx; status=$s"}
function Invoke-DbJsonFile([string]$SqlPath,[hashtable]$Vars){$dir=(Resolve-Path (Split-Path -Parent $SqlPath)).Path;$file=Split-Path -Leaf $SqlPath;$args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${dir}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")};$args += @('-f',"/sql/$file");$global:LASTEXITCODE=0;$out=docker @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "Database SQL failed: $file"};$raw=($out|ForEach-Object{[string]$_}) -join "`n";$idx=$raw.LastIndexOf('LGA03_JSON:');if($idx -ge 0){$json=$raw.Substring($idx+'LGA03_JSON:'.Length).Trim();return $json|ConvertFrom-Json};throw "No JSON result returned by $file. Raw=$raw"}

$script:base=$BaseUrl.TrimEnd('/');$plainPassword=Convert-Secret $Password
$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln=Join-Path $repo 'solidpos-platform.sln';$sqlPath=Join-Path $scriptRoot 'lga-03-limited-ga-multi-day-stability-burn-in-check.sql'
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1';$dashboardScript=Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtimeDir=Join-Path $repo '.runtime\lga-03-limited-ga-multi-day-stability-burn-in';$wpfSalesVm=Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\ViewModels\SalesViewModel.cs'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:Lga03ValidatorVersion"
Write-Step 'Repository/document LGA-03 guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing';Assert-True (Test-Path $sqlPath) 'LGA-03 SQL check missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-03-limited-ga-multi-day-stability-burn-in.md') @('lga-03','multi-day','burn-in','public ga not activated')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-03-burn-in-checkpoint-record.md') @('burn-in checkpoint','completed sales','payments','receipts')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-03-sync-stability-record.md') @('sync conflicts','dead letter','must not increase','baseline')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-03-inventory-shift-stability-record.md') @('negative stock','open shifts','zero','stability')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-03-go-no-go.md') @('go/no-go','public ga not activated','lga-04')
Write-Step 'Repository/document LGA-03 guardrails PASS'

Write-Step 'Local build/test/secret/WPF guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln };Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore };Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Assert-True (Test-Path $secretScan) 'Secret scan script missing';Invoke-Checked 'secret scan' { & $secretScan -Root $repo }
Assert-True (Test-Path $wpfSalesVm) 'SalesViewModel missing';$wpfSource=Get-Content -Raw $wpfSalesVm;Assert-True ($wpfSource.Contains('RefreshCommandStates')) 'WPF QSR command refresh helper missing';Assert-True ($wpfSource.Contains('RaiseCanExecuteChanged')) 'WPF QSR RaiseCanExecuteChanged calls missing';Assert-True ($WpfVisualConfirmed.IsPresent) 'WPF visual confirmation missing. Rerun with -WpfVisualConfirmed after confirming QSR cash flow remains enabled.'
if(-not $SkipDashboardBuild.IsPresent){Assert-True (Test-Path $dashboardScript) 'PosDashboard validation script missing';Invoke-Checked 'PosDashboard validation' { & $dashboardScript -SkipBuild:$false }}else{Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret/WPF guardrails PASS'
if($SkipLga02Revalidation.IsPresent){Write-Step 'LGA-02 prerequisite revalidation skipped by switch; LGA-03 requires existing LGA-02 PASS logs or runtime manifest.'}

Write-Step 'Limited GA burn-in API checks...'
$login=Invoke-Api 'POST' '/api/v1/auth/login' @{email=$Email;password=$plainPassword;tenantId=$TenantId};$accessToken=Req $login 'accessToken';$headers=@{Authorization="Bearer $accessToken"}
$healthLiveStatus=Get-HttpStatus 'GET' '/health/live';$healthReadyStatus=Get-HttpStatus 'GET' '/health/ready';Assert-2xx $healthLiveStatus 'health/live';Assert-2xx $healthReadyStatus 'health/ready'
$unauthObservabilityStatus=Get-HttpStatus 'GET' '/api/v1/observability/metrics';Assert-True ($unauthObservabilityStatus -eq 401) "unauthenticated observability must return 401; status=$unauthObservabilityStatus"
$metrics=Invoke-Api 'GET' '/api/v1/observability/metrics' $null $headers;$metricsDatabaseReady=[bool](Req (Req $metrics 'database') 'ready');Assert-True $metricsDatabaseReady 'metrics database.ready must be true'
$sync=Invoke-Api 'GET' '/api/v1/sync/status' $null $headers;$syncPendingCount=[int](Req $sync 'pendingCount');$syncProcessingCount=[int](Req $sync 'processingCount');$syncRetryPendingCount=[int](Req $sync 'retryPendingCount');$syncConflictCount=[int](Req $sync 'conflictCount');$syncDeadLetterCount=[int](Req $sync 'deadLetterCount')
Assert-True ($syncPendingCount -eq 0) "sync pendingCount must be 0; actual=$syncPendingCount";Assert-True ($syncProcessingCount -eq 0) "sync processingCount must be 0; actual=$syncProcessingCount";Assert-True ($syncRetryPendingCount -eq 0) "sync retryPendingCount must be 0; actual=$syncRetryPendingCount"
$contract=Invoke-Api 'GET' '/api/v1/sync/contract' $null $headers;$contractSchema=Optional-Prop $contract 'currentSchemaVersion';if($null -eq $contractSchema){$contractSchema=Optional-Prop $contract 'schemaVersion'};Assert-True ([int]$contractSchema -eq 4) "sync contract schema version must be 4; actual=$contractSchema"
$from=[uri]::EscapeDataString((Get-Date).ToUniversalTime().AddHours(-24).ToString('o'));$to=[uri]::EscapeDataString((Get-Date).ToUniversalTime().ToString('o'))
$salesRange=Invoke-Api 'GET' "/api/v1/reports/sales/range?from=$from&to=$to" $null $headers;$dashboardOverview=Invoke-Api 'GET' "/api/v1/reports/dashboard/overview?from=$from&to=$to&limit=20&trendBucket=day" $null $headers;$inventoryStatus=Get-HttpStatus 'GET' '/api/v1/inventory/stock' $headers
$dashboardUrlStatus=$null;if(-not [string]::IsNullOrWhiteSpace($DashboardUrl)){$dashboardUrlStatus=Get-HttpStatus 'GET' $DashboardUrl @{} -Absolute;Assert-True (($dashboardUrlStatus -ge 200 -and $dashboardUrlStatus -lt 400)) "DashboardUrl must return 2xx/3xx; status=$dashboardUrlStatus"}
Write-Step 'Limited GA burn-in API checks PASS'

Write-Step 'Database Limited GA burn-in snapshot...'
$dbVars=@{tenant_id=$TenantId;max_stores=$MaxStores;max_concurrent_terminals=$MaxConcurrentTerminals;allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount;allowed_dead_letters=$AllowedDeadLetterCount;allowed_waiting_connections=$AllowedWaitingConnectionCount}
$db=Invoke-DbJsonFile $sqlPath $dbVars
Write-Step 'Database Limited GA burn-in snapshot PASS'

Write-Step 'LGA-03 burn-in blocker matrix...'
$blockers=[ordered]@{};$conditions=New-Object System.Collections.Generic.List[string]
function Add-Blocker([string]$k,$v){$script:blockers[$k]=$v};function Add-Condition([string]$v){[void]$conditions.Add($v)}
$tenantState=Req $db 'tenantState';$rolloutScope=Req $db 'rolloutScope';$syncIntegrity=Req $db 'syncIntegrity';$financialIntegrity=Req $db 'financialIntegrity';$dbPressure=Req $db 'databasePressure';$rls=Req $db 'rls';$monitoringActivity=Req $db 'monitoringActivity';$negativeStock=Req $db 'negativeStock'
$activeStoreCount=[int64](Req $rolloutScope 'active_store_count');$openShiftCount=[int64](Req $rolloutScope 'open_shift_count');$activeStableReleaseCount=[int64](Req $rolloutScope 'active_stable_release_count');$availableTerminalCount=[int64](Req $rolloutScope 'available_terminal_count')
$legacySchemaEventCount=[int64](Req $syncIntegrity 'legacy_schema_event_count');$pendingConflictCount=[int64](Req $syncIntegrity 'pending_conflict_count');$retryPendingCount=[int64](Req $syncIntegrity 'retry_pending_count');$deadLetterCount=[int64](Req $syncIntegrity 'dead_letter_count');$staleProcessingCount=[int64](Req $syncIntegrity 'stale_processing_count')
$duplicateLocalSaleCount=[int64](Req $financialIntegrity 'duplicate_local_sale_count');$negativePaymentCount=[int64](Req $financialIntegrity 'negative_payment_count');$negativeStockCount=[int64](Req $negativeStock 'count');$waitingConnectionCount=[int64](Req $dbPressure 'waiting_connection_count');$longRunningQueryCount=[int64](Req $dbPressure 'long_running_query_count');$rlsMissingTableCount=[int64](Req $rls 'rls_missing_table_count')
$completedSales24h=[int64](Req $monitoringActivity 'completed_sales_24h');$payments24h=[int64](Req $monitoringActivity 'payments_24h');$receiptsIssued24h=[int64](Req $monitoringActivity 'receipts_issued_24h');$auditEvents24h=[int64](Req $monitoringActivity 'audit_events_24h')
$gaActivated=[bool](Req $db 'generalAvailabilityActivated');$publicGaActivated=[bool](Req $db 'publicGeneralAvailabilityActivated')
if(-not [bool](Req $db 'requiredTablesPresent')){Add-Blocker 'missing_required_tables' (Req $db 'missingRequiredTables')};if([int64](Req $tenantState 'active_tenant_count') -ne 1){Add-Blocker 'tenant_not_active' $tenantState}
if($activeStoreCount -lt 1){Add-Blocker 'no_active_store' $activeStoreCount};if($activeStoreCount -gt $MaxStores){Add-Blocker 'active_store_count_exceeds_limited_scope' @{actual=$activeStoreCount;max=$MaxStores}}
if($openShiftCount -gt $AllowedOpenShiftCount){Add-Blocker 'open_shift_count_above_burn_in_limit' @{actual=$openShiftCount;allowed=$AllowedOpenShiftCount}};if($activeStableReleaseCount -lt 1){Add-Blocker 'no_active_stable_release' $activeStableReleaseCount}
if($legacySchemaEventCount -ne 0){Add-Blocker 'legacy_schema_events' $legacySchemaEventCount};if($retryPendingCount -ne 0){Add-Blocker 'retry_pending_sync_events' $retryPendingCount};if($staleProcessingCount -ne 0){Add-Blocker 'stale_processing_sync_events' $staleProcessingCount};if($duplicateLocalSaleCount -ne 0){Add-Blocker 'duplicate_local_sales' $duplicateLocalSaleCount};if($negativePaymentCount -ne 0){Add-Blocker 'negative_payments' $negativePaymentCount};if($rlsMissingTableCount -ne 0){Add-Blocker 'rls_missing_tenant_tables' $rlsMissingTableCount};if($longRunningQueryCount -ne 0){Add-Blocker 'long_running_queries' $longRunningQueryCount}
if($pendingConflictCount -gt $AllowedExistingSyncConflictCount){Add-Blocker 'pending_conflicts_increased_above_baseline' @{actual=$pendingConflictCount;allowed=$AllowedExistingSyncConflictCount}};if($deadLetterCount -gt $AllowedDeadLetterCount){Add-Blocker 'dead_letters_increased_above_baseline' @{actual=$deadLetterCount;allowed=$AllowedDeadLetterCount}};if($negativeStockCount -gt $AllowedNegativeStockItemCount){Add-Blocker 'negative_stock_regression' @{actual=$negativeStockCount;allowed=$AllowedNegativeStockItemCount}};if($waitingConnectionCount -gt $AllowedWaitingConnectionCount){Add-Blocker 'waiting_connections_exceed_allowed_baseline' @{actual=$waitingConnectionCount;allowed=$AllowedWaitingConnectionCount}}
if($completedSales24h -lt $MinCompletedSalesInLast24h){Add-Blocker 'burn_in_sales_volume_below_minimum' @{actual=$completedSales24h;minimum=$MinCompletedSalesInLast24h}};if($payments24h -lt $MinPaymentsInLast24h){Add-Blocker 'burn_in_payment_volume_below_minimum' @{actual=$payments24h;minimum=$MinPaymentsInLast24h}};if($receiptsIssued24h -lt $MinReceiptsIssuedInLast24h){Add-Blocker 'burn_in_receipt_volume_below_minimum' @{actual=$receiptsIssued24h;minimum=$MinReceiptsIssuedInLast24h}}
if($gaActivated){Add-Blocker 'general_availability_flag_active' $true};if($publicGaActivated){Add-Blocker 'public_general_availability_flag_active' $true}
Add-Condition 'public_ga_not_activated_by_lga03';Add-Condition 'limited_ga_multi_day_burn_in_only';Add-Condition 'sync_conflict_count_must_not_increase_above_baseline';Add-Condition 'dead_letter_count_must_not_increase_above_baseline';Add-Condition 'negative_stock_must_remain_zero';Add-Condition 'open_shifts_must_remain_closed';Add-Condition 'ga09_capacity_boundary_concurrency_3_plus_carried_forward'
Assert-True ($blockers.Count -eq 0) ("LGA-03 blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
Write-Step 'LGA-03 burn-in blocker matrix PASS'

$status='PASS LGA-03 BURN-IN CHECKPOINT / CONTINUE LGA-03'
$nextPhase='LGA-03 - Continue multi-day burn-in checkpoints'
$finalizationCheckpointCount=0;$finalizationDistinctDateCount=0;$finalizationManifestCount=0
$checkpointDate=(Get-Date).ToString('yyyy-MM-dd')
if($FinalizeBurnIn.IsPresent){
  $existing=@(Get-ChildItem -Path $runtimeDir -Filter 'lga-03-burn-in-checkpoint-*.json' -ErrorAction SilentlyContinue)
  $dates=New-Object System.Collections.Generic.HashSet[string]
  foreach($f in $existing){try{$m=Get-Content -Raw $f.FullName|ConvertFrom-Json;if((Req $m 'blockerCount') -eq 0){[void]$dates.Add([string](Req $m 'checkpointDate'))}}catch{}}
  [void]$dates.Add($checkpointDate)
  $finalizationDistinctDateCount=$dates.Count;$finalizationManifestCount=$existing.Count + 1
  if($finalizationDistinctDateCount -lt $RequiredBurnInDays){Add-Blocker 'multi_day_burn_in_not_complete' @{distinctCheckpointDates=$finalizationDistinctDateCount;requiredBurnInDays=$RequiredBurnInDays}}
  Assert-True ($blockers.Count -eq 0) ("LGA-03 finalization blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
  $status='PASS LGA-03 LIMITED GA MULTI-DAY STABILITY BURN-IN / GO LGA-04';$nextPhase='LGA-04 - Limited GA Public GA Decision Readiness or Capacity Remediation'
}
$manifest=[ordered]@{phase='LGA-03';status=$status;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');checkpointDate=$checkpointDate;burnInCheckpoint=$BurnInCheckpoint;requiredBurnInDays=$RequiredBurnInDays;finalizeBurnIn=$FinalizeBurnIn.IsPresent;validatorVersion=$script:Lga03ValidatorVersion;entryGate='PASS LGA-02 LIMITED GA STABILITY LOOP AND OPERATIONAL CLEANUP / GO LGA-03';conflictDecision=$ConflictDecision;deadLetterDecision=$DeadLetterDecision;maxStores=$MaxStores;maxConcurrentTerminals=$MaxConcurrentTerminals;minCompletedSalesInLast24h=$MinCompletedSalesInLast24h;minPaymentsInLast24h=$MinPaymentsInLast24h;minReceiptsIssuedInLast24h=$MinReceiptsIssuedInLast24h;allowedExistingSyncConflictCount=$AllowedExistingSyncConflictCount;allowedDeadLetterCount=$AllowedDeadLetterCount;allowedNegativeStockItemCount=$AllowedNegativeStockItemCount;allowedOpenShiftCount=$AllowedOpenShiftCount;allowedWaitingConnectionCount=$AllowedWaitingConnectionCount;publicGeneralAvailabilityActivated=$false;generalAvailabilityActivated=$false;publicGaActivatedByValidator=$false;healthLiveStatus=$healthLiveStatus;healthReadyStatus=$healthReadyStatus;unauthenticatedObservabilityStatus=$unauthObservabilityStatus;inventoryStockStatus=$inventoryStatus;salesRangeCompletedSalesCount=[int](Req $salesRange 'completedSalesCount');dashboardOverviewCompletedSalesCount=[int](Req (Req $dashboardOverview 'sales') 'completedSalesCount');completedSalesInLast24h=$completedSales24h;paymentsInLast24h=$payments24h;receiptsIssuedInLast24h=$receiptsIssued24h;auditEventsInLast24h=$auditEvents24h;syncPendingCount=$syncPendingCount;syncProcessingCount=$syncProcessingCount;syncRetryPendingCount=$syncRetryPendingCount;syncConflictCount=$syncConflictCount;syncDeadLetterCount=$syncDeadLetterCount;dashboardUrlStatus=$dashboardUrlStatus;dashboardBuild=if($SkipDashboardBuild.IsPresent){'SKIPPED_BY_SWITCH'}else{'VALIDATED'};metricsDatabaseReady=$metricsDatabaseReady;syncContractCurrentSchemaVersion=[int]$contractSchema;databaseSnapshot=$db;negativeStockCount=$negativeStockCount;negativeStock=$negativeStock;activeStoreCount=$activeStoreCount;availableTerminalCount=$availableTerminalCount;openShiftCount=$openShiftCount;activeStableReleaseCount=$activeStableReleaseCount;duplicateLocalSaleCount=$duplicateLocalSaleCount;legacySchemaEventCount=$legacySchemaEventCount;pendingConflictCount=$pendingConflictCount;retryPendingCount=$retryPendingCount;deadLetterCount=$deadLetterCount;staleProcessingCount=$staleProcessingCount;rlsMissingTableCount=$rlsMissingTableCount;waitingConnectionCount=$waitingConnectionCount;longRunningQueryCount=$longRunningQueryCount;wpfQsrVisualConfirmed=$WpfVisualConfirmed.IsPresent;finalizationDistinctDateCount=$finalizationDistinctDateCount;finalizationManifestCount=$finalizationManifestCount;blockerCount=$blockers.Count;blockers=$blockers;conditions=@($conditions);schemaVersion=4;syncContract='schema_version_4';publicGaActivation='NOT_ACTIVATED';nextPhase=$nextPhase}
$safeLabel=('checkpoint-' + $BurnInCheckpoint + '-' + $checkpointDate) -replace '[^a-zA-Z0-9\-]','-'
$manifestPath=Join-Path $runtimeDir "lga-03-burn-in-$safeLabel.json";$manifest|ConvertTo-Json -Depth 80|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'LGA-03 evidence manifest and burn-in snapshot PASS'
Write-Step $status
$manifest|Format-List
