param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('FORMAL_ARCHIVE','REMEDIATED')][string]$ConflictDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('FORMAL_ARCHIVE','RETRY_OR_ARCHIVE')][string]$DeadLetterDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('REMEDIATE_BEFORE_PUBLIC_GA','FORMAL_ACCEPT_LIMITED_CAPACITY')][string]$CapacityDecision = 'REMEDIATE_BEFORE_PUBLIC_GA',
    [ValidateSet('KEEP_LIMITED_GA','READY_FOR_PUBLIC_GA_DECISION')][string]$PublicGaDecision = 'KEEP_LIMITED_GA',
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
    [int]$PublicGaReadinessConcurrency = 3,
    [int]$ConcurrencyProbeRequests = 6,
    [int]$MaxReadinessP95Ms = 1200,
    [switch]$WpfVisualConfirmed,
    [switch]$SkipDashboardBuild,
    [switch]$SkipLga03Revalidation
)
$ErrorActionPreference='Stop'
$script:Lga04ValidatorVersion='LGA-04.3-formal-limited-capacity-acceptance-contract'
function Write-Step([string]$m){Write-Host "[LGA-04] $m"}
function Assert-True($c,[string]$m){if(-not $c){throw $m}}
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;& $c;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "$n failed with exit code $e"}}
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{},[int]$TimeoutSec=30){$p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=$TimeoutSec};if($null -ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 80;$p.ContentType='application/json'};try{return Invoke-RestMethod @p}catch{$status='';$text='';if($_.Exception.Response){try{$status="; httpStatus=$([int]$_.Exception.Response.StatusCode)"}catch{};try{$r=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream());$text=$r.ReadToEnd();$r.Close()}catch{}};if($text){$status="$status; response=$text"};throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"}}
function Get-HttpStatus([string]$Method,[string]$UriOrPath,[hashtable]$Headers=@{},[switch]$Absolute,[int]$TimeoutSec=30){$u=if($Absolute.IsPresent){$UriOrPath}else{"$script:base$UriOrPath"};try{$r=Invoke-WebRequest -Method $Method -Uri $u -Headers $Headers -TimeoutSec $TimeoutSec -UseBasicParsing;return [int]$r.StatusCode}catch{if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
function Req($o,[string]$n){Assert-True ($null -ne $o) "Object missing while checking property $n";$p=$o.PSObject.Properties[$n];Assert-True ($null -ne $p) "Required property missing: $n";return $p.Value}
function Assert-DocumentContains([string]$Path,[string[]]$Terms){Assert-True (Test-Path $Path) "Required document missing: $Path";$c=(Get-Content -Raw $Path).ToLowerInvariant();foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"}}
function Assert-2xx([int]$s,[string]$n){Assert-True (($s -ge 200 -and $s -lt 300)) "$n must return 2xx; status=$s"}
function Invoke-DbJsonFile([string]$SqlPath,[hashtable]$Vars,[string]$Marker){
  $file=(Resolve-Path $SqlPath).Path
  $cmd=Get-Command psql -ErrorAction SilentlyContinue
  if($cmd){
    $args=@($DatabaseUrl,'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
    foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")}
    $args += @('-f',$file)
    $global:LASTEXITCODE=0;$out=& $cmd.Source @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0
    if($e -ne 0){throw "Database SQL failed via psql: $(Split-Path -Leaf $file)"}
  } else {
    $docker=Get-Command docker -ErrorAction SilentlyContinue
    Assert-True ($null -ne $docker) 'Neither psql nor docker is available. Install PostgreSQL client tools or Docker Desktop, or run from the original validation machine.'
    $dir=(Resolve-Path (Split-Path -Parent $SqlPath)).Path;$leaf=Split-Path -Leaf $SqlPath
    $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${dir}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
    foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")}
    $args += @('-f',"/sql/$leaf")
    $global:LASTEXITCODE=0;$out=& $docker.Source @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0
    if($e -ne 0){throw "Database SQL failed via docker: $leaf"}
  }
  $raw=($out|ForEach-Object{[string]$_}) -join "`n";$idx=$raw.LastIndexOf($Marker)
  if($idx -ge 0){$json=$raw.Substring($idx+$Marker.Length).Trim();return $json|ConvertFrom-Json}
  throw "No JSON result returned by $(Split-Path -Leaf $file). Raw=$raw"
}
function Invoke-ConcurrencyProbe([string]$Path,[int]$Concurrency,[int]$Requests,[int]$TimeoutSec=20){
  $uri="$script:base$Path"
  $jobs=@()
  $results=@()
  for($i=0;$i -lt $Requests;$i++){
    while(@($jobs | Where-Object { $_.State -eq 'Running' }).Count -ge $Concurrency){
      [void](Wait-Job -Job @($jobs) -Any -Timeout 1)
      foreach($j in @($jobs | Where-Object { $_.State -ne 'Running' })){
        $received=@(Receive-Job -Job $j -ErrorAction SilentlyContinue)
        Remove-Job -Job $j -Force -ErrorAction SilentlyContinue
        $jobs=@($jobs | Where-Object { $_.Id -ne $j.Id })
        foreach($item in $received){ if($null -ne $item){ $results += $item } }
      }
    }
    $jobs += Start-Job -ScriptBlock {
      param([string]$u,[int]$timeout)
      $sw=[Diagnostics.Stopwatch]::StartNew();$status=0;$err=''
      try{
        $resp=Invoke-WebRequest -Uri $u -Method GET -TimeoutSec $timeout -UseBasicParsing
        $status=[int]$resp.StatusCode
      }catch{
        if($_.Exception.Response){try{$status=[int]$_.Exception.Response.StatusCode}catch{$status=0}}else{$status=0}
        $err=$_.Exception.Message
      }
      $sw.Stop()
      [pscustomobject]@{status=[int]$status;ms=[int]$sw.ElapsedMilliseconds;error=[string]$err}
    } -ArgumentList @($uri,[int]$TimeoutSec)
  }
  while(@($jobs).Count -gt 0){
    [void](Wait-Job -Job @($jobs) -Any -Timeout 1)
    foreach($j in @($jobs | Where-Object { $_.State -ne 'Running' })){
      $received=@(Receive-Job -Job $j -ErrorAction SilentlyContinue)
      Remove-Job -Job $j -Force -ErrorAction SilentlyContinue
      $jobs=@($jobs | Where-Object { $_.Id -ne $j.Id })
      foreach($item in $received){ if($null -ne $item){ $results += $item } }
    }
  }
  $arr=@($results)
  $success=@($arr | Where-Object { ([int]$_.status) -ge 200 -and ([int]$_.status) -lt 300 })
  $lat=@($success | ForEach-Object { [int]$_.ms } | Sort-Object)
  [int]$p95=0
  if($lat.Count -gt 0){
    [int]$idx=[int]([Math]::Ceiling([double]$lat.Count * 0.95) - 1)
    if($idx -lt 0){$idx=0}
    if($idx -ge $lat.Count){$idx=$lat.Count-1}
    $p95=[int]$lat[$idx]
  }
  $statusGroups=@($arr | Group-Object -Property status | ForEach-Object {
    $statusValue=0
    [void][int]::TryParse([string]$_.Name,[ref]$statusValue)
    [pscustomobject]@{status=[int]$statusValue;count=[int]$_.Count}
  })
  return [pscustomobject]@{
    path=[string]$Path
    concurrency=[int]$Concurrency
    requests=[int]$Requests
    successCount=[int]$success.Count
    failureCount=[int]($arr.Count-$success.Count)
    p95Ms=[int]$p95
    statuses=$statusGroups
  }
}

$script:base=$BaseUrl.TrimEnd('/');$plainPassword=Convert-Secret $Password
$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln=Join-Path $repo 'solidpos-platform.sln';$sqlPath=Join-Path $scriptRoot 'lga-04-public-ga-decision-readiness-capacity-remediation-check.sql'
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1';$dashboardScript=Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtimeDir=Join-Path $repo '.runtime\lga-04-public-ga-decision-readiness-capacity-remediation';$lga03RuntimeDir=Join-Path $repo '.runtime\lga-03-limited-ga-multi-day-stability-burn-in'
$wpfSalesVm=Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\ViewModels\SalesViewModel.cs'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:Lga04ValidatorVersion"
Write-Step 'Repository/document LGA-04 guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing';Assert-True (Test-Path $sqlPath) 'LGA-04 SQL check missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-04-public-ga-decision-readiness-capacity-remediation.md') @('lga-04','public ga decision','capacity remediation','public ga not activated')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-04-capacity-remediation-plan.md') @('capacity boundary','concurrency','railway','remediation')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-04-public-ga-go-no-go.md') @('go/no-go','keep limited ga','public ga not activated','lga-05')
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-04-evidence-matrix.md') @('evidence matrix','lga-03','capacity','decision')
Write-Step 'Repository/document LGA-04 guardrails PASS'

Write-Step 'LGA-03 finalization prerequisite...'
if($SkipLga03Revalidation.IsPresent){Write-Step 'LGA-03 prerequisite revalidation skipped by switch; LGA-04 requires existing LGA-03 PASS logs.'}
else{
  Assert-True (Test-Path $lga03RuntimeDir) 'LGA-03 runtime manifest directory missing. Rerun LGA-03 finalization or use -SkipLga03Revalidation only if PASS logs are available.'
  $lga03Manifests=@(Get-ChildItem -Path $lga03RuntimeDir -Filter '*.json' -ErrorAction SilentlyContinue)
  $lga03Pass=$false
  foreach($f in $lga03Manifests){try{$m=Get-Content -Raw $f.FullName|ConvertFrom-Json;if(([string](Req $m 'status')).Contains('PASS LGA-03 LIMITED GA MULTI-DAY STABILITY BURN-IN / GO LGA-04')){$lga03Pass=$true}}catch{}}
  Assert-True $lga03Pass 'LGA-03 final PASS manifest not found. LGA-04 requires LGA-03 GO LGA-04.'
}
Write-Step 'LGA-03 finalization prerequisite PASS'

Write-Step 'Local build/test/secret/WPF guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln };Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore };Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Assert-True (Test-Path $secretScan) 'Secret scan script missing';Invoke-Checked 'secret scan' { & $secretScan -Root $repo }
Assert-True (Test-Path $wpfSalesVm) 'SalesViewModel missing';$wpfSource=Get-Content -Raw $wpfSalesVm;Assert-True ($wpfSource.Contains('RefreshCommandStates')) 'WPF QSR command refresh helper missing';Assert-True ($wpfSource.Contains('RaiseCanExecuteChanged')) 'WPF QSR RaiseCanExecuteChanged calls missing';Assert-True ($WpfVisualConfirmed.IsPresent) 'WPF visual confirmation missing. Rerun with -WpfVisualConfirmed after confirming QSR cash flow remains enabled.'
if(-not $SkipDashboardBuild.IsPresent){Assert-True (Test-Path $dashboardScript) 'PosDashboard validation script missing';Invoke-Checked 'PosDashboard validation' { & $dashboardScript -SkipBuild:$false }}else{Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret/WPF guardrails PASS'

Write-Step 'Limited GA public decision API checks...'
$login=Invoke-Api 'POST' '/api/v1/auth/login' @{email=$Email;password=$plainPassword;tenantId=$TenantId};$accessToken=Req $login 'accessToken';$headers=@{Authorization="Bearer $accessToken"}
$healthLiveStatus=Get-HttpStatus 'GET' '/health/live';$healthReadyStatus=Get-HttpStatus 'GET' '/health/ready';Assert-2xx $healthLiveStatus 'health/live';Assert-2xx $healthReadyStatus 'health/ready'
$unauthObservabilityStatus=Get-HttpStatus 'GET' '/api/v1/observability/metrics';Assert-True ($unauthObservabilityStatus -eq 401) "unauthenticated observability must return 401; status=$unauthObservabilityStatus"
$metrics=Invoke-Api 'GET' '/api/v1/observability/metrics' $null $headers;$metricsDatabaseReady=[bool](Req (Req $metrics 'database') 'ready');Assert-True $metricsDatabaseReady 'metrics database.ready must be true'
$sync=Invoke-Api 'GET' '/api/v1/sync/status' $null $headers;$syncPendingCount=[int](Req $sync 'pendingCount');$syncProcessingCount=[int](Req $sync 'processingCount');$syncRetryPendingCount=[int](Req $sync 'retryPendingCount');$syncConflictCount=[int](Req $sync 'conflictCount');$syncDeadLetterCount=[int](Req $sync 'deadLetterCount')
Assert-True ($syncPendingCount -eq 0) "sync pendingCount must be 0; actual=$syncPendingCount";Assert-True ($syncProcessingCount -eq 0) "sync processingCount must be 0; actual=$syncProcessingCount";Assert-True ($syncRetryPendingCount -eq 0) "sync retryPendingCount must be 0; actual=$syncRetryPendingCount"
$contract=Invoke-Api 'GET' '/api/v1/sync/contract' $null $headers
$contractSchema=$null
if($contract.PSObject.Properties['schemaVersion']){$contractSchema=[int]$contract.schemaVersion}
elseif($contract.PSObject.Properties['currentSchemaVersion']){$contractSchema=[int]$contract.currentSchemaVersion}
elseif($contract.PSObject.Properties['syncContract'] -and ([string]$contract.syncContract) -eq 'schema_version_4'){$contractSchema=4}
elseif($contract.PSObject.Properties['contract'] -and ([string]$contract.contract) -eq 'schema_version_4'){$contractSchema=4}
else{$contractSchema=4;Write-Step 'Sync contract endpoint does not expose schemaVersion; deferring authoritative schema assertion to DB snapshot.'}
Assert-True ($contractSchema -eq 4) "sync contract schema must resolve to 4; actual=$contractSchema"
$inventoryStatus=Get-HttpStatus 'GET' '/api/v1/inventory/stock' $headers;Assert-2xx $inventoryStatus 'inventory stock'
$from=[DateTime]::UtcNow.AddHours(-24).ToString('o');$to=[DateTime]::UtcNow.ToString('o')
$salesRange=Invoke-Api 'GET' ("/api/v1/reports/sales/range?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))") $null $headers
$dashboardOverview=Invoke-Api 'GET' ("/api/v1/reports/dashboard/overview?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))&limit=20&trendBucket=day") $null $headers
$dashboardUrlStatus=$null;if(-not [string]::IsNullOrWhiteSpace($DashboardUrl)){$dashboardUrlStatus=Get-HttpStatus 'GET' $DashboardUrl @{} -Absolute;Assert-2xx $dashboardUrlStatus 'dashboard url'}
Write-Step 'Limited GA public decision API checks PASS'

Write-Step 'Capacity boundary probe...'
$liveProbe=Invoke-ConcurrencyProbe '/health/live' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
$readyProbe=Invoke-ConcurrencyProbe '/health/ready' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
$capacityProbePassed=($liveProbe.failureCount -eq 0 -and $readyProbe.failureCount -eq 0 -and $readyProbe.p95Ms -le $MaxReadinessP95Ms)
if($capacityProbePassed){Write-Step 'Capacity boundary probe PASS'}else{Write-Step 'Capacity boundary probe indicates remediation or formal limited-capacity acceptance is required'}

Write-Step 'Database Limited GA public decision snapshot...'
$db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;max_stores=$MaxStores;max_concurrent_terminals=$MaxConcurrentTerminals;allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount;allowed_dead_letters=$AllowedDeadLetterCount;allowed_waiting_connections=$AllowedWaitingConnectionCount} 'LGA04_JSON:'
Assert-True ([int](Req $db 'schemaVersion') -eq 4) 'DB snapshot schemaVersion must be 4';Assert-True (([string](Req $db 'syncContract')) -eq 'schema_version_4') 'DB snapshot syncContract must be schema_version_4'
Write-Step 'Database Limited GA public decision snapshot PASS'

Write-Step 'LGA-04 public GA decision blocker matrix...'
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
if($openShiftCount -gt $AllowedOpenShiftCount){Add-Blocker 'open_shift_count_above_limit' @{actual=$openShiftCount;allowed=$AllowedOpenShiftCount}};if($activeStableReleaseCount -lt 1){Add-Blocker 'no_active_stable_release' $activeStableReleaseCount}
if($legacySchemaEventCount -ne 0){Add-Blocker 'legacy_schema_events' $legacySchemaEventCount};if($retryPendingCount -ne 0){Add-Blocker 'retry_pending_sync_events' $retryPendingCount};if($staleProcessingCount -ne 0){Add-Blocker 'stale_processing_sync_events' $staleProcessingCount};if($duplicateLocalSaleCount -ne 0){Add-Blocker 'duplicate_local_sales' $duplicateLocalSaleCount};if($negativePaymentCount -ne 0){Add-Blocker 'negative_payments' $negativePaymentCount};if($rlsMissingTableCount -ne 0){Add-Blocker 'rls_missing_tenant_tables' $rlsMissingTableCount};if($longRunningQueryCount -ne 0){Add-Blocker 'long_running_queries' $longRunningQueryCount}
if($pendingConflictCount -gt $AllowedExistingSyncConflictCount){Add-Blocker 'pending_conflicts_increased_above_baseline' @{actual=$pendingConflictCount;allowed=$AllowedExistingSyncConflictCount}};if($deadLetterCount -gt $AllowedDeadLetterCount){Add-Blocker 'dead_letters_increased_above_baseline' @{actual=$deadLetterCount;allowed=$AllowedDeadLetterCount}};if($negativeStockCount -gt $AllowedNegativeStockItemCount){Add-Blocker 'negative_stock_regression' @{actual=$negativeStockCount;allowed=$AllowedNegativeStockItemCount}};if($waitingConnectionCount -gt $AllowedWaitingConnectionCount){Add-Blocker 'waiting_connections_exceed_allowed_baseline' @{actual=$waitingConnectionCount;allowed=$AllowedWaitingConnectionCount}}
if($completedSales24h -lt $MinCompletedSalesInLast24h){Add-Blocker 'decision_window_sales_volume_below_minimum' @{actual=$completedSales24h;minimum=$MinCompletedSalesInLast24h}};if($payments24h -lt $MinPaymentsInLast24h){Add-Blocker 'decision_window_payment_volume_below_minimum' @{actual=$payments24h;minimum=$MinPaymentsInLast24h}};if($receiptsIssued24h -lt $MinReceiptsIssuedInLast24h){Add-Blocker 'decision_window_receipt_volume_below_minimum' @{actual=$receiptsIssued24h;minimum=$MinReceiptsIssuedInLast24h}}
if($gaActivated){Add-Blocker 'general_availability_flag_active' $true};if($publicGaActivated){Add-Blocker 'public_general_availability_flag_active' $true}
if((-not $capacityProbePassed) -and @('REMEDIATE_BEFORE_PUBLIC_GA','FORMAL_ACCEPT_LIMITED_CAPACITY') -notcontains $CapacityDecision){Add-Blocker 'capacity_boundary_failed_without_remediation_decision' @{liveProbe=$liveProbe;readyProbe=$readyProbe;capacityDecision=$CapacityDecision}}
if(($PublicGaDecision -eq 'READY_FOR_PUBLIC_GA_DECISION') -and (-not $capacityProbePassed)){Add-Blocker 'public_ga_decision_requested_while_capacity_not_passing' @{publicGaDecision=$PublicGaDecision;capacityDecision=$CapacityDecision}}
Add-Condition 'public_ga_not_activated_by_lga04';Add-Condition 'lga03_multi_day_burn_in_closed';Add-Condition 'capacity_boundary_must_be_remediated_or_formally_accepted';Add-Condition 'public_ga_decision_only_no_activation';Add-Condition 'schema_version_4_required';Add-Condition 'sync_conflict_and_dead_letter_baselines_carried_forward'
Assert-True ($blockers.Count -eq 0) ("LGA-04 blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
Write-Step 'LGA-04 public GA decision blocker matrix PASS'

$status='PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA - CAPACITY REMEDIATION REQUIRED'
$nextPhase='LGA-04 Capacity Remediation - increase Railway capacity and rerun LGA-04'
if((-not $capacityProbePassed) -and $CapacityDecision -eq 'FORMAL_ACCEPT_LIMITED_CAPACITY' -and $PublicGaDecision -eq 'KEEP_LIMITED_GA'){$status='PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA - FORMAL LIMITED CAPACITY ACCEPTED';$nextPhase='LGA-05 - Continue Limited GA with formal capacity risk accepted'}
if($capacityProbePassed -and $PublicGaDecision -eq 'READY_FOR_PUBLIC_GA_DECISION'){$status='PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / READY FOR PUBLIC GA DECISION PACKAGE';$nextPhase='LGA-05 - Public GA Approval Package (manual activation gate)'}
elseif($capacityProbePassed){$status='PASS LGA-04 LIMITED GA PUBLIC GA DECISION READINESS / KEEP LIMITED GA';$nextPhase='LGA-05 - Public GA Approval Package or continue Limited GA'}
$manifest=[ordered]@{phase='LGA-04';status=$status;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');checkpointDate=(Get-Date).ToString('yyyy-MM-dd');validatorVersion=$script:Lga04ValidatorVersion;entryGate='PASS LGA-03 LIMITED GA MULTI-DAY STABILITY BURN-IN / GO LGA-04';conflictDecision=$ConflictDecision;deadLetterDecision=$DeadLetterDecision;capacityDecision=$CapacityDecision;publicGaDecision=$PublicGaDecision;maxStores=$MaxStores;maxConcurrentTerminals=$MaxConcurrentTerminals;publicGaReadinessConcurrency=$PublicGaReadinessConcurrency;concurrencyProbeRequests=$ConcurrencyProbeRequests;maxReadinessP95Ms=$MaxReadinessP95Ms;capacityProbePassed=$capacityProbePassed;healthLiveConcurrencyProbe=$liveProbe;healthReadyConcurrencyProbe=$readyProbe;minCompletedSalesInLast24h=$MinCompletedSalesInLast24h;minPaymentsInLast24h=$MinPaymentsInLast24h;minReceiptsIssuedInLast24h=$MinReceiptsIssuedInLast24h;allowedExistingSyncConflictCount=$AllowedExistingSyncConflictCount;allowedDeadLetterCount=$AllowedDeadLetterCount;allowedNegativeStockItemCount=$AllowedNegativeStockItemCount;allowedOpenShiftCount=$AllowedOpenShiftCount;allowedWaitingConnectionCount=$AllowedWaitingConnectionCount;publicGeneralAvailabilityActivated=$false;generalAvailabilityActivated=$false;publicGaActivatedByValidator=$false;healthLiveStatus=$healthLiveStatus;healthReadyStatus=$healthReadyStatus;unauthenticatedObservabilityStatus=$unauthObservabilityStatus;inventoryStockStatus=$inventoryStatus;salesRangeCompletedSalesCount=[int](Req $salesRange 'completedSalesCount');dashboardOverviewCompletedSalesCount=[int](Req (Req $dashboardOverview 'sales') 'completedSalesCount');completedSalesInLast24h=$completedSales24h;paymentsInLast24h=$payments24h;receiptsIssuedInLast24h=$receiptsIssued24h;auditEventsInLast24h=$auditEvents24h;syncPendingCount=$syncPendingCount;syncProcessingCount=$syncProcessingCount;syncRetryPendingCount=$syncRetryPendingCount;syncConflictCount=$syncConflictCount;syncDeadLetterCount=$syncDeadLetterCount;dashboardUrlStatus=$dashboardUrlStatus;dashboardBuild=if($SkipDashboardBuild.IsPresent){'SKIPPED_BY_SWITCH'}else{'VALIDATED'};metricsDatabaseReady=$metricsDatabaseReady;syncContractCurrentSchemaVersion=[int]$contractSchema;databaseSnapshot=$db;negativeStockCount=$negativeStockCount;negativeStock=$negativeStock;activeStoreCount=$activeStoreCount;availableTerminalCount=$availableTerminalCount;openShiftCount=$openShiftCount;activeStableReleaseCount=$activeStableReleaseCount;duplicateLocalSaleCount=$duplicateLocalSaleCount;legacySchemaEventCount=$legacySchemaEventCount;pendingConflictCount=$pendingConflictCount;retryPendingCount=$retryPendingCount;deadLetterCount=$deadLetterCount;staleProcessingCount=$staleProcessingCount;rlsMissingTableCount=$rlsMissingTableCount;waitingConnectionCount=$waitingConnectionCount;longRunningQueryCount=$longRunningQueryCount;wpfQsrVisualConfirmed=$WpfVisualConfirmed.IsPresent;blockerCount=$blockers.Count;blockers=$blockers;conditions=@($conditions);schemaVersion=4;syncContract='schema_version_4';publicGaActivation='NOT_ACTIVATED';nextPhase=$nextPhase}
$manifestPath=Join-Path $runtimeDir ("lga-04-public-ga-decision-readiness-" + (Get-Date).ToString('yyyy-MM-dd-HHmmss') + ".json");$manifest|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'LGA-04 evidence manifest and public decision snapshot PASS'
Write-Step $status
$manifest|Format-List
