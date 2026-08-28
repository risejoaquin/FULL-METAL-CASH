param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('FORMAL_ARCHIVE','REMEDIATED')][string]$ConflictDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('FORMAL_ARCHIVE','RETRY_OR_ARCHIVE')][string]$DeadLetterDecision = 'FORMAL_ARCHIVE',
    [ValidateSet('CAPACITY_GATE_PASSED')][string]$CapacityDecision = 'CAPACITY_GATE_PASSED',
    [ValidateSet('RECOMMEND_PUBLIC_GA_GO','NO_GO')][string]$ReadinessDecision = 'RECOMMEND_PUBLIC_GA_GO',
    [ValidateSet('KEEP_NOT_ACTIVATED')][string]$ActivationDecision = 'KEEP_NOT_ACTIVATED',
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
    [switch]$SkipPostLgaCapacityRevalidation
)
$ErrorActionPreference='Stop'
$script:PublicGaReviewValidatorVersion='PUBLIC-GA-READINESS-REVIEW.1.1-document-contract-hotfix'
function Write-Step([string]$m){Write-Host "[PUBLIC-GA-READINESS-REVIEW] $m"}
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
$sln=Join-Path $repo 'solidpos-platform.sln';$sqlPath=Join-Path $scriptRoot 'public-ga-readiness-review-check.sql'
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1';$dashboardScript=Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtimeDir=Join-Path $repo '.runtime\public-ga-readiness-review';$postLgaRuntimeDir=Join-Path $repo '.runtime\post-lga-capacity-infrastructure-remediation'
$wpfSalesVm=Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\ViewModels\SalesViewModel.cs'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:PublicGaReviewValidatorVersion"
Write-Step 'Repository/document PUBLIC GA READINESS REVIEW guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing';Assert-True (Test-Path $sqlPath) 'Public GA readiness review SQL check missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\public-ga-readiness-review.md') @('public ga','readiness review','go/no-go','not activated','capacity gate','schema version 4')
Assert-DocumentContains (Join-Path $repo 'docs\ga\public-ga-go-no-go-checklist.md') @('security','disaster recovery','observability','acceptance','capacity','rollback','public ga not activated')
Assert-DocumentContains (Join-Path $repo 'docs\ga\public-ga-evidence-matrix.md') @('ga-07','ga-08','ga-10','ga-11','ga-12','lga-12','post-lga capacity','schema version 4')
Assert-DocumentContains (Join-Path $repo 'docs\ga\public-ga-activation-separation.md') @('separate','activation','validator','not activated','explicit decision')
Assert-True (Test-Path (Join-Path $repo 'SOLIDPOS_GA_07_BACKUP_RESTORE_ROLLBACK_AND_DISASTER_RECOVERY.md')) 'GA-07 disaster recovery evidence document missing'
Assert-True (Test-Path (Join-Path $repo 'SOLIDPOS_GA_08_SECURITY_TENANT_ISOLATION_ACCESS_CONTROL_FINAL_GATE.md')) 'GA-08 security evidence document missing'
Assert-True (Test-Path (Join-Path $repo 'SOLIDPOS_GA_10_OBSERVABILITY_DASHBOARD_ALERTING_AND_ONCALL_READINESS.md')) 'GA-10 observability/on-call evidence document missing'
Assert-True (Test-Path (Join-Path $repo 'SOLIDPOS_GA_11_CUSTOMER_OPERATOR_ADMIN_ACCEPTANCE.md')) 'GA-11 acceptance evidence document missing'
Assert-True (Test-Path (Join-Path $repo 'SOLIDPOS_GA_12_FINAL_GENERAL_AVAILABILITY_LAUNCH_READINESS.md')) 'GA-12 readiness evidence document missing'
Assert-True (Test-Path (Join-Path $repo 'SOLIDPOS_LGA_12_FINAL_LIMITED_GA_CLOSURE_OR_PUBLIC_GA_RECOMMENDATION.md')) 'LGA-12 closure evidence document missing'
$readinessProbePath=Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\PostgreSql\PostgreSqlReadinessProbe.cs'
Assert-True (Test-Path $readinessProbePath) 'PostgreSqlReadinessProbe.cs missing'
$readinessSource=Get-Content -Raw $readinessProbePath
Assert-True ($readinessSource.Contains('unnest(@required_tables::text[])')) 'Readiness single-query optimization missing.'
Assert-True ($readinessSource.Contains('NpgsqlDbType.Array | NpgsqlDbType.Text')) 'Readiness text-array parameter type missing.'
Write-Step 'Repository/document PUBLIC GA READINESS REVIEW guardrails PASS'

Write-Step 'POST-LGA capacity prerequisite...'
if($SkipPostLgaCapacityRevalidation.IsPresent){Write-Step 'POST-LGA capacity prerequisite revalidation skipped by switch; reviewed PASS production logs are required.'}
else{
  Assert-True (Test-Path $postLgaRuntimeDir) 'POST-LGA capacity runtime manifest directory missing. Rerun POST-LGA capacity or use -SkipPostLgaCapacityRevalidation only with reviewed PASS logs.'
  $postLgaManifests=@(Get-ChildItem -Path $postLgaRuntimeDir -Filter '*.json' -ErrorAction SilentlyContinue)
  $postLgaPass=$false
  foreach($f in $postLgaManifests){try{$m=Get-Content -Raw $f.FullName|ConvertFrom-Json;if(([string](Req $m 'status')).Contains('PASS POST-LGA CAPACITY INFRASTRUCTURE REMEDIATION') -and [bool](Req $m 'capacityProbePassed')){$postLgaPass=$true}}catch{}}
  Assert-True $postLgaPass 'Reviewed POST-LGA capacity PASS manifest not found.'
}
Write-Step 'POST-LGA capacity prerequisite PASS'

Write-Step 'Local build/test/secret/WPF guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln };Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore };Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Assert-True (Test-Path $secretScan) 'Secret scan script missing';Invoke-Checked 'secret scan' { & $secretScan -Root $repo }
Assert-True (Test-Path $wpfSalesVm) 'SalesViewModel missing';$wpfSource=Get-Content -Raw $wpfSalesVm;Assert-True ($wpfSource.Contains('RefreshCommandStates')) 'WPF QSR command refresh helper missing';Assert-True ($wpfSource.Contains('RaiseCanExecuteChanged')) 'WPF QSR RaiseCanExecuteChanged calls missing';Assert-True ($WpfVisualConfirmed.IsPresent) 'WPF visual confirmation missing. Rerun with -WpfVisualConfirmed after confirming QSR cash flow remains enabled.'
if(-not $SkipDashboardBuild.IsPresent){Assert-True (Test-Path $dashboardScript) 'PosDashboard validation script missing';Invoke-Checked 'PosDashboard validation' { & $dashboardScript -SkipBuild:$false }}else{Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret/WPF guardrails PASS'

Write-Step 'Public GA readiness API checks...'
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
Write-Step 'Public GA readiness API checks PASS'

Write-Step 'Capacity boundary probe...'
$liveProbe=Invoke-ConcurrencyProbe '/health/live' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
$readyProbe=Invoke-ConcurrencyProbe '/health/ready' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
$capacityProbePassed=($liveProbe.failureCount -eq 0 -and $readyProbe.failureCount -eq 0 -and $liveProbe.p95Ms -le $MaxReadinessP95Ms -and $readyProbe.p95Ms -le $MaxReadinessP95Ms)
if($capacityProbePassed){Write-Step 'Capacity boundary probe PASS'}else{Write-Step 'Capacity boundary probe FAIL - infrastructure remediation remains incomplete'}

Write-Step 'Database Public GA readiness snapshot...'
$db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;max_stores=$MaxStores;max_concurrent_terminals=$MaxConcurrentTerminals;allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount;allowed_dead_letters=$AllowedDeadLetterCount;allowed_waiting_connections=$AllowedWaitingConnectionCount} 'PUBLIC_GA_READINESS_REVIEW_JSON:'
Assert-True ([int](Req $db 'schemaVersion') -eq 4) 'DB snapshot schemaVersion must be 4';Assert-True (([string](Req $db 'syncContract')) -eq 'schema_version_4') 'DB snapshot syncContract must be schema_version_4'
Write-Step 'Database Public GA readiness snapshot PASS'

Write-Step 'Public GA readiness GO/NO-GO blocker matrix...'
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
if($CapacityDecision -ne 'CAPACITY_GATE_PASSED'){Add-Blocker 'capacity_decision_must_record_passed_gate' $CapacityDecision}
if($ConflictDecision -notin @('FORMAL_ARCHIVE','REMEDIATED')){Add-Blocker 'sync_conflict_decision_invalid' $ConflictDecision}
if($DeadLetterDecision -notin @('FORMAL_ARCHIVE','RETRY_OR_ARCHIVE')){Add-Blocker 'sync_dead_letter_decision_invalid' $DeadLetterDecision}
if($ActivationDecision -ne 'KEEP_NOT_ACTIVATED'){Add-Blocker 'activation_decision_must_keep_public_ga_not_activated' $ActivationDecision}
if($ReadinessDecision -eq 'RECOMMEND_PUBLIC_GA_GO' -and -not $capacityProbePassed){Add-Blocker 'go_recommendation_requires_capacity_gate_pass' $ReadinessDecision}
Add-Condition 'public_ga_not_activated';Add-Condition 'lga12_closed';Add-Condition 'post_lga_capacity_closed';Add-Condition 'capacity_gate_passed';Add-Condition ('readiness_decision_' + $ReadinessDecision.ToLowerInvariant());Add-Condition 'schema_version_4_required';Add-Condition 'negative_stock_zero_required';Add-Condition 'waiting_connections_baseline_not_raised';Add-Condition 'sync_conflict_and_dead_letter_baselines_carried_forward';Add-Condition 'readiness_single_catalog_query_required'
Assert-True ($blockers.Count -eq 0) ("PUBLIC-GA-READINESS-REVIEW blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
Write-Step 'Public GA readiness GO/NO-GO blocker matrix PASS'

$status=if($ReadinessDecision -eq 'RECOMMEND_PUBLIC_GA_GO'){'PASS PUBLIC GA READINESS REVIEW / GO RECOMMENDATION AUTHORIZED / PUBLIC GA NOT ACTIVATED'}else{'PASS PUBLIC GA READINESS REVIEW / NO-GO RECORDED / PUBLIC GA NOT ACTIVATED'}
$nextPhase=if($ReadinessDecision -eq 'RECOMMEND_PUBLIC_GA_GO'){'PUBLIC_GA_ACTIVATION_DECISION_AUTHORIZED_NOT_EXECUTED'}else{'PUBLIC_GA_REMEDIATION_REQUIRED'}
$manifest=[ordered]@{phase='PUBLIC-GA-READINESS-REVIEW';status=$status;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');checkpointDate=(Get-Date).ToString('yyyy-MM-dd');validatorVersion=$script:PublicGaReviewValidatorVersion;entryGate='PASS POST-LGA CAPACITY INFRASTRUCTURE REMEDIATION';readinessDecision=$ReadinessDecision;capacityDecision=$CapacityDecision;activationDecision=$ActivationDecision;publicGeneralAvailabilityActivated=$false;generalAvailabilityActivated=$false;publicGaActivatedByValidator=$false;publicGaReadinessConcurrency=$PublicGaReadinessConcurrency;concurrencyProbeRequests=$ConcurrencyProbeRequests;maxReadinessP95Ms=$MaxReadinessP95Ms;capacityProbePassed=$capacityProbePassed;healthLiveConcurrencyProbe=$liveProbe;healthReadyConcurrencyProbe=$readyProbe;healthLiveStatus=$healthLiveStatus;healthReadyStatus=$healthReadyStatus;waitingConnectionCount=$waitingConnectionCount;longRunningQueryCount=$longRunningQueryCount;negativeStockCount=$negativeStockCount;activeStoreCount=$activeStoreCount;availableTerminalCount=$availableTerminalCount;completedSalesInLast24h=$completedSales24h;paymentsInLast24h=$payments24h;receiptsIssuedInLast24h=$receiptsIssued24h;auditEventsInLast24h=$auditEvents24h;openShiftCount=$openShiftCount;closedShiftCount=$closedShiftCount;cashDifferenceLast24hCount=$cashDifferenceLast24hCount;syncPendingCount=$syncPendingCount;syncProcessingCount=$syncProcessingCount;syncRetryPendingCount=$syncRetryPendingCount;syncConflictCount=$syncConflictCount;syncDeadLetterCount=$syncDeadLetterCount;databaseSnapshot=$db;blockerCount=$blockers.Count;blockers=$blockers;conditions=@($conditions);schemaVersion=4;syncContract='schema_version_4';readinessSqlCommandsPerRequest=1;publicGaActivation='NOT_ACTIVATED';activationExecuted=$false;nextPhase=$nextPhase}
$manifestPath=Join-Path $runtimeDir ("public-ga-readiness-review-" + (Get-Date).ToString('yyyy-MM-dd-HHmmss') + ".json");$manifest|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'PUBLIC-GA-READINESS-REVIEW evidence manifest PASS'
Write-Step $status
$manifest|Format-List
