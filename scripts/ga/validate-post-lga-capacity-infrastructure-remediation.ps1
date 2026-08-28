param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('FORMAL_ARCHIVE','REMEDIATED')][string]$ConflictDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('FORMAL_ARCHIVE','RETRY_OR_ARCHIVE')][string]$DeadLetterDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('REMEDIATE_BEFORE_PUBLIC_GA')][string]$CapacityDecision = 'REMEDIATE_BEFORE_PUBLIC_GA',
    [ValidateSet('KEEP_LIMITED_GA')][string]$PublicGaDecision = 'KEEP_LIMITED_GA',
    [int]$MaxStores = 2,
    [int]$MaxConcurrentTerminals = 2,
    [int]$MinCompletedSalesInLast24h = 6,
    [int]$MinPaymentsInLast24h = 6,
    [int]$MinReceiptsIssuedInLast24h = 3,
    [int]$MinAuditEventsInLast24h = 1,
    [int]$MinClosedShiftsTotal = 1,
    [int]$AllowedCashDifferenceLast24hCount = 0,
    [int]$AllowedExistingSyncConflictCount = 3,
    [int]$AllowedDeadLetterCount = 1,
    [int]$AllowedNegativeStockItemCount = 0,
    [int]$AllowedOpenShiftCount = 0,
    [int]$AllowedWaitingConnectionCount = 12,
    [int]$PublicGaReadinessConcurrency = 3,
    [int]$ConcurrencyProbeRequests = 6,
    [int]$MaxReadinessP95Ms = 1200,
    [switch]$WpfVisualConfirmed,
    [switch]$SkipDashboardBuild,
    [switch]$SkipLga12Revalidation
)
$ErrorActionPreference='Stop'
$script:PostLgaCapacityValidatorVersion='POST-LGA-CAPACITY.0.2-validator-latency-contract-restoration'
function Write-Step([string]$m){Write-Host "[POST-LGA-CAPACITY] $m"}
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
  $curl=(Get-Command curl.exe -ErrorAction SilentlyContinue)
  if(-not $curl){throw 'curl.exe is required for latency probes so PowerShell job startup time is excluded from HTTP latency.'}
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
      param([string]$curlPath,[string]$u,[int]$timeout)
      $status=0;$ms=0;$err=''
      try{
        $format='__SOLIDPOS_METRIC__ %{http_code} %{time_total}'
        $output=& $curlPath '-sS' '-o' 'NUL' '--max-time' ([string]$timeout) '-w' $format $u 2>&1
        $exitCode=$LASTEXITCODE
        $line=(@($output | ForEach-Object {[string]$_}) | Where-Object {$_ -like '__SOLIDPOS_METRIC__*'} | Select-Object -Last 1)
        if($line -match '^__SOLIDPOS_METRIC__\s+(\d{3})\s+([0-9.]+)$'){
          $status=[int]$Matches[1]
          $seconds=[double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture)
          $ms=[int][Math]::Round($seconds*1000.0)
        } else {
          $err=(@($output | ForEach-Object {[string]$_}) -join ' | ')
        }
        if($exitCode -ne 0 -and -not $err){$err="curl exit code $exitCode"}
      }catch{$err=$_.Exception.Message}
      [pscustomobject]@{status=[int]$status;ms=[int]$ms;error=[string]$err}
    } -ArgumentList @($curl.Source,$uri,[int]$TimeoutSec)
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
$sln=Join-Path $repo 'solidpos-platform.sln';$sqlPath=Join-Path $scriptRoot 'post-lga-capacity-infrastructure-remediation-check.sql'
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1';$dashboardScript=Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtimeDir=Join-Path $repo '.runtime\post-lga-capacity-infrastructure-remediation';$lga12RuntimeDir=Join-Path $repo '.runtime\lga-12-final-limited-ga-closure-or-public-ga-recommendation'
$wpfSalesVm=Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\ViewModels\SalesViewModel.cs'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:PostLgaCapacityValidatorVersion"
Write-Step 'Repository/document POST-LGA-CAPACITY guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing';Assert-True (Test-Path $sqlPath) 'Post-LGA capacity SQL check missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\post-lga-capacity-infrastructure-remediation.md') @('lga-12','capacity','infrastructure','concurrency 3','1200','public ga not activated','schema version 4')
Assert-DocumentContains (Join-Path $repo 'docs\ga\post-lga-capacity-remediation-plan.md') @('readiness query','railway','postgresql','waiting connections','/health/live','/health/ready','capacity gate')
Assert-DocumentContains (Join-Path $repo 'docs\ga\post-lga-capacity-validation-checklist.md') @('concurrency 3','p95','negative stock','sync queues','commercial operations','public ga not activated')
Assert-DocumentContains (Join-Path $repo 'docs\ga\post-lga-capacity-evidence-matrix.md') @('evidence matrix','lga-12','schema version 4','capacity gate passed','public ga not activated')
$readinessProbePath=Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\PostgreSql\PostgreSqlReadinessProbe.cs'
Assert-True (Test-Path $readinessProbePath) 'PostgreSqlReadinessProbe.cs missing'
$readinessSource=Get-Content -Raw $readinessProbePath
Assert-True ($readinessSource.Contains('unnest(@required_tables::text[])')) 'Readiness optimization missing: required tables must be validated in one catalog query.'
Assert-True ($readinessSource.Contains('NpgsqlDbType.Array | NpgsqlDbType.Text')) 'Readiness optimization missing explicit text-array parameter type.'
Assert-True (-not $readinessSource.Contains('foreach (string table in RequiredTables)')) 'Legacy per-table readiness query loop is still present.'
Write-Step 'Repository/document POST-LGA-CAPACITY guardrails PASS'

Write-Step 'LGA-12 prerequisite...'
if($SkipLga12Revalidation.IsPresent){Write-Step 'LGA-12 prerequisite revalidation skipped by switch; POST-LGA-CAPACITY requires existing LGA-12 PASS logs and reviewed readiness decision.'}
else{
  Assert-True (Test-Path $lga12RuntimeDir) 'LGA-12 runtime manifest directory missing. Rerun LGA-12 or use -SkipLga12Revalidation only if reviewed PASS logs are available.'
  $lga12Manifests=@(Get-ChildItem -Path $lga12RuntimeDir -Filter '*.json' -ErrorAction SilentlyContinue)
  $lga12Pass=$false
  foreach($f in $lga12Manifests){try{$m=Get-Content -Raw $f.FullName|ConvertFrom-Json;if(([string](Req $m 'status')).Contains('PASS LGA-12 FINAL LIMITED GA CLOSURE')){$lga12Pass=$true}}catch{}}
  Assert-True $lga12Pass 'LGA-12 PASS manifest not found. POST-LGA-CAPACITY requires reviewed LGA-12 closure.'
}
Write-Step 'LGA-12 prerequisite PASS'

Write-Step 'Local build/test/secret/WPF guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln };Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore };Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Assert-True (Test-Path $secretScan) 'Secret scan script missing';Invoke-Checked 'secret scan' { & $secretScan -Root $repo }
Assert-True (Test-Path $wpfSalesVm) 'SalesViewModel missing';$wpfSource=Get-Content -Raw $wpfSalesVm;Assert-True ($wpfSource.Contains('RefreshCommandStates')) 'WPF QSR command refresh helper missing';Assert-True ($wpfSource.Contains('RaiseCanExecuteChanged')) 'WPF QSR RaiseCanExecuteChanged calls missing';Assert-True ($WpfVisualConfirmed.IsPresent) 'WPF visual confirmation missing. Rerun with -WpfVisualConfirmed after confirming QSR cash flow remains enabled.'
if(-not $SkipDashboardBuild.IsPresent){Assert-True (Test-Path $dashboardScript) 'PosDashboard validation script missing';Invoke-Checked 'PosDashboard validation' { & $dashboardScript -SkipBuild:$false }}else{Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret/WPF guardrails PASS'

Write-Step 'Post-LGA capacity remediation API checks...'
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
Write-Step 'Post-LGA capacity remediation API checks PASS'

Write-Step 'Capacity boundary probe...'
$liveProbe=Invoke-ConcurrencyProbe '/health/live' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
$readyProbe=Invoke-ConcurrencyProbe '/health/ready' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
$capacityProbePassed=($liveProbe.failureCount -eq 0 -and $readyProbe.failureCount -eq 0 -and $liveProbe.p95Ms -le $MaxReadinessP95Ms -and $readyProbe.p95Ms -le $MaxReadinessP95Ms)
if($capacityProbePassed){Write-Step 'Capacity boundary probe PASS'}else{Write-Step 'Capacity boundary probe FAIL - infrastructure remediation remains incomplete'}

Write-Step 'Database Post-LGA capacity remediation snapshot...'
$db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;max_stores=$MaxStores;max_concurrent_terminals=$MaxConcurrentTerminals;allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount;allowed_dead_letters=$AllowedDeadLetterCount;allowed_waiting_connections=$AllowedWaitingConnectionCount} 'POST_LGA_CAPACITY_JSON:'
Assert-True ([int](Req $db 'schemaVersion') -eq 4) 'DB snapshot schemaVersion must be 4';Assert-True (([string](Req $db 'syncContract')) -eq 'schema_version_4') 'DB snapshot syncContract must be schema_version_4'
Write-Step 'Database Post-LGA capacity remediation snapshot PASS'

Write-Step 'Post-LGA capacity remediation blocker matrix...'
$blockers=[ordered]@{};$conditions=New-Object System.Collections.Generic.List[string]
function Add-Blocker([string]$k,$v){$script:blockers[$k]=$v};function Add-Condition([string]$v){[void]$conditions.Add($v)}
$tenantState=Req $db 'tenantState';$rolloutScope=Req $db 'rolloutScope';$syncIntegrity=Req $db 'syncIntegrity';$financialIntegrity=Req $db 'financialIntegrity';$dbPressure=Req $db 'databasePressure';$rls=Req $db 'rls';$monitoringActivity=Req $db 'monitoringActivity';$negativeStock=Req $db 'negativeStock'
$activeStoreCount=[int64](Req $rolloutScope 'active_store_count');$openShiftCount=[int64](Req $rolloutScope 'open_shift_count');$closedShiftCount=[int64](Req $rolloutScope 'closed_shift_count');$cashDifferenceLast24hCount=[int64](Req $rolloutScope 'cash_difference_last_24h_count');$activeStableReleaseCount=[int64](Req $rolloutScope 'active_stable_release_count');$availableTerminalCount=[int64](Req $rolloutScope 'available_terminal_count')
$legacySchemaEventCount=[int64](Req $syncIntegrity 'legacy_schema_event_count');$pendingConflictCount=[int64](Req $syncIntegrity 'pending_conflict_count');$retryPendingCount=[int64](Req $syncIntegrity 'retry_pending_count');$deadLetterCount=[int64](Req $syncIntegrity 'dead_letter_count');$staleProcessingCount=[int64](Req $syncIntegrity 'stale_processing_count')
$duplicateLocalSaleCount=[int64](Req $financialIntegrity 'duplicate_local_sale_count');$negativePaymentCount=[int64](Req $financialIntegrity 'negative_payment_count');$negativeStockCount=[int64](Req $negativeStock 'count');$waitingConnectionCount=[int64](Req $dbPressure 'waiting_connection_count');$longRunningQueryCount=[int64](Req $dbPressure 'long_running_query_count');$rlsMissingTableCount=[int64](Req $rls 'rls_missing_table_count')
$completedSales24h=[int64](Req $monitoringActivity 'completed_sales_24h');$payments24h=[int64](Req $monitoringActivity 'payments_24h');$receiptsIssued24h=[int64](Req $monitoringActivity 'receipts_issued_24h');$auditEvents24h=[int64](Req $monitoringActivity 'audit_events_24h')
$gaActivated=[bool](Req $db 'generalAvailabilityActivated');$publicGaActivated=[bool](Req $db 'publicGeneralAvailabilityActivated')
if(-not [bool](Req $db 'requiredTablesPresent')){Add-Blocker 'missing_required_tables' (Req $db 'missingRequiredTables')};if([int64](Req $tenantState 'active_tenant_count') -ne 1){Add-Blocker 'tenant_not_active' $tenantState}
if($activeStoreCount -lt 1){Add-Blocker 'no_active_store' $activeStoreCount};if($activeStoreCount -gt $MaxStores){Add-Blocker 'active_store_count_exceeds_limited_scope' @{actual=$activeStoreCount;max=$MaxStores}};if($availableTerminalCount -lt 1){Add-Blocker 'no_available_terminal' $availableTerminalCount}
if($openShiftCount -gt $AllowedOpenShiftCount){Add-Blocker 'open_shift_count_above_limit' @{actual=$openShiftCount;allowed=$AllowedOpenShiftCount}};if($closedShiftCount -lt $MinClosedShiftsTotal){Add-Blocker 'commercial_cash_shift_history_below_minimum' @{actual=$closedShiftCount;minimum=$MinClosedShiftsTotal}};if($cashDifferenceLast24hCount -gt $AllowedCashDifferenceLast24hCount){Add-Blocker 'cash_difference_last_24h_above_limit' @{actual=$cashDifferenceLast24hCount;allowed=$AllowedCashDifferenceLast24hCount}};if($activeStableReleaseCount -lt 1){Add-Blocker 'no_active_stable_release' $activeStableReleaseCount}
if($legacySchemaEventCount -ne 0){Add-Blocker 'legacy_schema_events' $legacySchemaEventCount};if($retryPendingCount -ne 0){Add-Blocker 'retry_pending_sync_events' $retryPendingCount};if($staleProcessingCount -ne 0){Add-Blocker 'stale_processing_sync_events' $staleProcessingCount};if($duplicateLocalSaleCount -ne 0){Add-Blocker 'duplicate_local_sales' $duplicateLocalSaleCount};if($negativePaymentCount -ne 0){Add-Blocker 'negative_payments' $negativePaymentCount};if($rlsMissingTableCount -ne 0){Add-Blocker 'rls_missing_tenant_tables' $rlsMissingTableCount};if($longRunningQueryCount -ne 0){Add-Blocker 'long_running_queries' $longRunningQueryCount}
if($pendingConflictCount -gt $AllowedExistingSyncConflictCount){Add-Blocker 'pending_conflicts_increased_above_baseline' @{actual=$pendingConflictCount;allowed=$AllowedExistingSyncConflictCount}};if($deadLetterCount -gt $AllowedDeadLetterCount){Add-Blocker 'dead_letters_increased_above_baseline' @{actual=$deadLetterCount;allowed=$AllowedDeadLetterCount}};if($negativeStockCount -gt $AllowedNegativeStockItemCount){Add-Blocker 'negative_stock_regression' @{actual=$negativeStockCount;allowed=$AllowedNegativeStockItemCount}};if($waitingConnectionCount -gt $AllowedWaitingConnectionCount){Add-Blocker 'waiting_connections_exceed_allowed_baseline' @{actual=$waitingConnectionCount;allowed=$AllowedWaitingConnectionCount}}
if($completedSales24h -lt $MinCompletedSalesInLast24h){Add-Blocker 'decision_window_sales_volume_below_minimum' @{actual=$completedSales24h;minimum=$MinCompletedSalesInLast24h}};if($payments24h -lt $MinPaymentsInLast24h){Add-Blocker 'decision_window_payment_volume_below_minimum' @{actual=$payments24h;minimum=$MinPaymentsInLast24h}};if($receiptsIssued24h -lt $MinReceiptsIssuedInLast24h){Add-Blocker 'decision_window_receipt_volume_below_minimum' @{actual=$receiptsIssued24h;minimum=$MinReceiptsIssuedInLast24h}};if($auditEvents24h -lt $MinAuditEventsInLast24h){Add-Blocker 'decision_window_audit_volume_below_minimum' @{actual=$auditEvents24h;minimum=$MinAuditEventsInLast24h}}
if($gaActivated){Add-Blocker 'general_availability_flag_active' $true};if($publicGaActivated){Add-Blocker 'public_general_availability_flag_active' $true}
if(-not $capacityProbePassed){Add-Blocker 'public_ga_capacity_gate_not_met_after_remediation' @{liveProbe=$liveProbe;readyProbe=$readyProbe;maxReadinessP95Ms=$MaxReadinessP95Ms;requiredConcurrency=$PublicGaReadinessConcurrency}}
if($CapacityDecision -ne 'REMEDIATE_BEFORE_PUBLIC_GA'){Add-Blocker 'capacity_decision_must_require_remediation' $CapacityDecision}
if($PublicGaDecision -ne 'KEEP_LIMITED_GA'){Add-Blocker 'public_ga_decision_must_keep_limited_ga' $PublicGaDecision}
Add-Condition 'public_ga_not_activated';Add-Condition 'lga12_closed';Add-Condition 'limited_ga_only';Add-Condition 'capacity_remediation_required';Add-Condition 'capacity_gate_must_pass';Add-Condition 'schema_version_4_required';Add-Condition 'negative_stock_zero_required';Add-Condition 'waiting_connections_baseline_not_raised';Add-Condition 'sync_conflict_and_dead_letter_baselines_carried_forward';Add-Condition 'readiness_single_catalog_query_required'
Assert-True ($blockers.Count -eq 0) ("POST-LGA-CAPACITY blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
Write-Step 'Post-LGA capacity remediation blocker matrix PASS'

$status='PASS POST-LGA CAPACITY INFRASTRUCTURE REMEDIATION / CAPACITY GATE PASSED / LIMITED GA RETAINED / PUBLIC GA NOT ACTIVATED'
$nextPhase='PUBLIC_GA_READINESS_REVIEW_AUTHORIZED_NOT_ACTIVATED'
$manifest=[ordered]@{phase='POST-LGA-CAPACITY';status=$status;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');checkpointDate=(Get-Date).ToString('yyyy-MM-dd');validatorVersion=$script:PostLgaCapacityValidatorVersion;entryGate='PASS LGA-12 FINAL LIMITED GA CLOSURE';capacityDecision=$CapacityDecision;publicGaDecision=$PublicGaDecision;publicGeneralAvailabilityActivated=$false;generalAvailabilityActivated=$false;publicGaActivatedByValidator=$false;publicGaReadinessConcurrency=$PublicGaReadinessConcurrency;concurrencyProbeRequests=$ConcurrencyProbeRequests;maxReadinessP95Ms=$MaxReadinessP95Ms;capacityProbePassed=$capacityProbePassed;healthLiveConcurrencyProbe=$liveProbe;healthReadyConcurrencyProbe=$readyProbe;healthLiveStatus=$healthLiveStatus;healthReadyStatus=$healthReadyStatus;waitingConnectionCount=$waitingConnectionCount;longRunningQueryCount=$longRunningQueryCount;negativeStockCount=$negativeStockCount;activeStoreCount=$activeStoreCount;availableTerminalCount=$availableTerminalCount;completedSalesInLast24h=$completedSales24h;paymentsInLast24h=$payments24h;receiptsIssuedInLast24h=$receiptsIssued24h;auditEventsInLast24h=$auditEvents24h;openShiftCount=$openShiftCount;closedShiftCount=$closedShiftCount;cashDifferenceLast24hCount=$cashDifferenceLast24hCount;syncPendingCount=$syncPendingCount;syncProcessingCount=$syncProcessingCount;syncRetryPendingCount=$syncRetryPendingCount;syncConflictCount=$syncConflictCount;syncDeadLetterCount=$syncDeadLetterCount;databaseSnapshot=$db;blockerCount=$blockers.Count;blockers=$blockers;conditions=@($conditions);schemaVersion=4;syncContract='schema_version_4';readinessSqlCommandsPerRequest=1;publicGaActivation='NOT_ACTIVATED';nextPhase=$nextPhase}
$manifestPath=Join-Path $runtimeDir ("post-lga-capacity-infrastructure-remediation-" + (Get-Date).ToString('yyyy-MM-dd-HHmmss') + ".json");$manifest|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'POST-LGA-CAPACITY evidence manifest and infrastructure-remediation snapshot PASS'
Write-Step $status
$manifest|Format-List
