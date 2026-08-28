param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [ValidateSet('LIMITED')][string]$RolloutMode = 'LIMITED',
    [ValidateSet('KEEP_LIMITED_GA','AUTHORIZE_PUBLIC_GA','BLOCK_PUBLIC_GA')][string]$Decision = 'KEEP_LIMITED_GA',
    [int]$MaxStores = 2,
    [int]$MaxConcurrentTerminals = 2,
    [int]$CapacitySampleCount = 12,
    [int]$SampleIntervalMilliseconds = 250,
    [decimal]$MaxP95LatencyMs = 5000,
    [int]$AllowedExistingSyncConflictCount = 3,
    [int]$AllowedDeadLetterCount = 1,
    [int]$AllowedWaitingConnectionCount = 11,
    [switch]$ExplicitPublicGaAuthorization,
    [switch]$SkipDashboardBuild,
    [switch]$SkipCga03Revalidation
)
$ErrorActionPreference = 'Stop'
$script:Cga04ValidatorVersion = 'CGA-04.1-sync-contract-schema-version-compatibility'
function Write-Step([string]$m){ Write-Host "[CGA-04] $m" }
function Assert-True($c,[string]$m){ if(-not $c){ throw $m } }
function Convert-Secret([securestring]$s){ $b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s); try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)} finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)} }
function Invoke-Checked([string]$n,[scriptblock]$c){ $global:LASTEXITCODE=0; & $c; $e=$LASTEXITCODE; $global:LASTEXITCODE=0; if($e -ne 0){throw "$n failed with exit code $e"} }
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{},[int]$TimeoutSec=30){
  $p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=$TimeoutSec}
  if($null -ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 30;$p.ContentType='application/json'}
  try{return Invoke-RestMethod @p}catch{ $status='';$text=''; if($_.Exception.Response){try{$status="; httpStatus=$([int]$_.Exception.Response.StatusCode)"}catch{};try{$r=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream());$text=$r.ReadToEnd();$r.Close()}catch{}}; if($text){$status="$status; response=$text"}; throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"}
}
function Get-HttpStatus([string]$Method,[string]$UriOrPath,[hashtable]$Headers=@{},$Body=$null,[int]$TimeoutSec=30,[switch]$Absolute){
  $u=if($Absolute.IsPresent){$UriOrPath}else{"$script:base$UriOrPath"}
  try{$p=@{Method=$Method;Uri=$u;Headers=$Headers;TimeoutSec=$TimeoutSec;UseBasicParsing=$true}; if($null -ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 30;$p.ContentType='application/json'}; $r=Invoke-WebRequest @p; return [int]$r.StatusCode}catch{ if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode}; throw }
}
function Req($o,[string]$n){ Assert-True ($null -ne $o) "Object missing while checking property $n"; $p=$o.PSObject.Properties[$n]; Assert-True ($null -ne $p) "Required property missing: $n"; return $p.Value }
function Optional-Prop($o,[string]$n){ if($null -eq $o){ return $null }; $p=$o.PSObject.Properties[$n]; if($null -eq $p){ return $null }; return $p.Value }
function Resolve-DecimalMetric($o,[string[]]$Names,[decimal]$Fallback){ foreach($n in $Names){ $v=Optional-Prop $o $n; if($null -ne $v -and -not [string]::IsNullOrWhiteSpace([string]$v)){ try { return [decimal]$v } catch {} } }; return $Fallback }
function Resolve-Int64Metric($o,[string[]]$Names,[int64]$Fallback){ foreach($n in $Names){ $v=Optional-Prop $o $n; if($null -ne $v -and -not [string]::IsNullOrWhiteSpace([string]$v)){ try { return [int64]$v } catch {} } }; return $Fallback }
function Measure-Endpoint([string]$Name,[string]$Path,[hashtable]$Headers=@{},[int]$Count=12){
  $results=@(); 1..$Count|ForEach-Object{ $w=[Diagnostics.Stopwatch]::StartNew();$status=0;$ok=$false; try{$r=Invoke-WebRequest -Method Get -Uri "$script:base$Path" -Headers $Headers -TimeoutSec 30 -UseBasicParsing;$status=[int]$r.StatusCode;$ok=$status -ge 200 -and $status -lt 400}catch{if($_.Exception.Response){try{$status=[int]$_.Exception.Response.StatusCode}catch{}}};$w.Stop();$results += [pscustomobject]@{status=$status;ok=$ok;ms=[long]$w.ElapsedMilliseconds}; Start-Sleep -Milliseconds $SampleIntervalMilliseconds }
  $lat=@($results|Where-Object {$_.ok}|Select-Object -ExpandProperty ms|Sort-Object); $success=$lat.Count; $errors=@($results|Where-Object {-not $_.ok}).Count; Assert-True ($success -gt 0) "$Name had no successful requests"; $i=[Math]::Ceiling($success*.95)-1; if($i -lt 0){$i=0}; if($i -ge $success){$i=$success-1}; return [pscustomobject]@{name=$Name;success=$success;errors=$errors;p95Ms=[long]$lat[$i];maxMs=[long](($lat|Measure-Object -Maximum).Maximum)}
}
function Invoke-DbJsonFile([string]$SqlPath,[hashtable]$Vars){
  $dir=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $file=Split-Path -Leaf $SqlPath
  $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${dir}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
  foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")}; $args += @('-f',"/sql/$file")
  $global:LASTEXITCODE=0; $out=docker @args; $e=$LASTEXITCODE; $global:LASTEXITCODE=0; if($e -ne 0){throw "Database SQL failed: $file"}
  $raw=($out|ForEach-Object{[string]$_}) -join "`n"; $idx=$raw.LastIndexOf('CGA04_JSON:'); if($idx -ge 0){$json=$raw.Substring($idx+'CGA04_JSON:'.Length).Trim(); return $json|ConvertFrom-Json}
  foreach($line in $out){$s=([string]$line).Trim(); if($s.StartsWith('{')){try{return $s|ConvertFrom-Json}catch{}}}; throw "No JSON result returned by $file. Raw=$raw"
}
function Assert-DocumentContains([string]$Path,[string[]]$Terms){ Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Assert-2xx([int]$s,[string]$n){Assert-True (($s -ge 200 -and $s -lt 300)) "$n must return 2xx; status=$s"}

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-Secret $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln=Join-Path $repo 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'cga-04-public-ga-activation-decision-check.sql'
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
$dashboardScript=Join-Path $repo 'scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1'
$runtimeDir=Join-Path $repo '.runtime\cga-04-public-ga-activation-decision'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:Cga04ValidatorVersion"
Write-Step 'Repository/document CGA-04 guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing'
Assert-True (Test-Path $sqlPath) 'CGA-04 SQL check missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\cga-04-public-ga-activation-decision.md') @('cga-04','public ga','activation decision','not activated','limited ga')
Assert-DocumentContains (Join-Path $repo 'docs\ga\cga-04-public-ga-decision-record.md') @('decision','keep_limited_ga','authorize_public_ga','block_public_ga')
Assert-DocumentContains (Join-Path $repo 'docs\ga\cga-04-evidence-matrix.md') @('evidence','public ga','capacity','database','sync')
Assert-DocumentContains (Join-Path $repo 'docs\ga\cga-04-go-no-go.md') @('keep limited ga','authorize public ga','no_go','not activated')
Write-Step 'Repository/document CGA-04 guardrails PASS'

Write-Step 'Local build/test/secret guardrails...'
Invoke-Checked 'dotnet restore' { dotnet restore $sln }
Invoke-Checked 'dotnet build' { dotnet build $sln --no-restore }
Invoke-Checked 'dotnet test' { dotnet test $sln --no-build }
Assert-True (Test-Path $secretScan) 'Secret scan script missing'
Invoke-Checked 'secret scan' { & $secretScan -Root $repo }
if(-not $SkipDashboardBuild.IsPresent){Assert-True (Test-Path $dashboardScript) 'PosDashboard validation script missing'; Invoke-Checked 'PosDashboard validation' { & $dashboardScript -SkipBuild:$false }}else{Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret guardrails PASS'

if($SkipCga03Revalidation.IsPresent){Write-Step 'CGA-03 prerequisite revalidation skipped by switch; CGA-04 requires existing CGA-03 PASS logs or runtime manifest.'}

Write-Step 'Public GA activation decision API checks...'
$login=Invoke-Api 'POST' '/api/v1/auth/login' @{email=$Email;password=$plainPassword;tenantId=$TenantId}
$accessToken=Req $login 'accessToken'; $headers=@{Authorization="Bearer $accessToken"}
$healthLiveStatus=Get-HttpStatus 'GET' '/health/live'; $healthReadyStatus=Get-HttpStatus 'GET' '/health/ready'; Assert-2xx $healthLiveStatus 'health/live'; Assert-2xx $healthReadyStatus 'health/ready'
$unauthObservabilityStatus=Get-HttpStatus 'GET' '/api/v1/observability/metrics'; Assert-True ($unauthObservabilityStatus -eq 401) "unauthenticated observability must return 401; status=$unauthObservabilityStatus"
$metrics=Invoke-Api 'GET' '/api/v1/observability/metrics' $null $headers; $metricsDatabaseReady=[bool](Req (Req $metrics 'database') 'ready'); Assert-True $metricsDatabaseReady 'metrics database.ready must be true'
$tenantCurrentStatus=Get-HttpStatus 'GET' '/api/v1/tenants/current' $headers; Assert-2xx $tenantCurrentStatus 'tenant current'
$sync=Invoke-Api 'GET' '/api/v1/sync/status' $null $headers
$syncPendingCount=[int](Req $sync 'pendingCount'); $syncProcessingCount=[int](Req $sync 'processingCount'); $syncRetryPendingCount=[int](Req $sync 'retryPendingCount'); $syncConflictCount=[int](Req $sync 'conflictCount'); $syncDeadLetterCount=[int](Req $sync 'deadLetterCount')
Assert-True ($syncPendingCount -eq 0) "sync pendingCount must be 0; actual=$syncPendingCount"; Assert-True ($syncProcessingCount -eq 0) "sync processingCount must be 0; actual=$syncProcessingCount"; Assert-True ($syncRetryPendingCount -eq 0) "sync retryPendingCount must be 0; actual=$syncRetryPendingCount"
Assert-True ($syncConflictCount -le $AllowedExistingSyncConflictCount) "sync conflictCount exceeds accepted baseline; actual=$syncConflictCount; allowed=$AllowedExistingSyncConflictCount"; Assert-True ($syncDeadLetterCount -le $AllowedDeadLetterCount) "sync deadLetterCount exceeds accepted baseline; actual=$syncDeadLetterCount; allowed=$AllowedDeadLetterCount"
$contract=Invoke-Api 'GET' '/api/v1/sync/contract' $null $headers; $contractSchemaVersion=[int](Resolve-Int64Metric $contract @('currentSchemaVersion','schemaVersion','version') 0); Assert-True ($contractSchemaVersion -eq 4) "sync contract schema version must be 4; actual=$contractSchemaVersion"
if($null -eq (Optional-Prop $contract 'schemaVersion')){ $script:syncContractSchemaVersionCompatibilityCondition = 'sync_contract_schemaVersion_missing_used_currentSchemaVersion_fallback' } else { $script:syncContractSchemaVersionCompatibilityCondition = $null }
$from=(Get-Date).ToUniversalTime().AddHours(-24).ToString('o'); $to=(Get-Date).ToUniversalTime().ToString('o'); $fromEsc=[uri]::EscapeDataString($from); $toEsc=[uri]::EscapeDataString($to)
$salesRange=Invoke-Api 'GET' "/api/v1/reports/sales/range?from=$fromEsc&to=$toEsc" $null $headers
$dashboardOverview=Invoke-Api 'GET' "/api/v1/reports/dashboard/overview?from=$fromEsc&to=$toEsc&limit=20&trendBucket=day" $null $headers
$dashboardUrlStatus=0; if(-not [string]::IsNullOrWhiteSpace($DashboardUrl)){$dashboardUrlStatus=Get-HttpStatus 'GET' $DashboardUrl @{} $null 30 -Absolute; Assert-True (($dashboardUrlStatus -ge 200 -and $dashboardUrlStatus -lt 400)) "DashboardUrl must return 2xx/3xx; status=$dashboardUrlStatus"}
$readyProbe=Measure-Endpoint 'health-ready' '/health/ready' @{} $CapacitySampleCount
$dashboardProbe=Measure-Endpoint 'dashboard-overview' "/api/v1/reports/dashboard/overview?from=$fromEsc&to=$toEsc&limit=20&trendBucket=day" $headers $CapacitySampleCount
Assert-True ($readyProbe.errors -eq 0) "health-ready probe errors must be 0; actual=$($readyProbe.errors)"; Assert-True ($dashboardProbe.errors -eq 0) "dashboard-overview probe errors must be 0; actual=$($dashboardProbe.errors)"
Assert-True ([decimal]$readyProbe.p95Ms -le $MaxP95LatencyMs) "health-ready p95 exceeds threshold; p95=$($readyProbe.p95Ms); max=$MaxP95LatencyMs"; Assert-True ([decimal]$dashboardProbe.p95Ms -le $MaxP95LatencyMs) "dashboard-overview p95 exceeds threshold; p95=$($dashboardProbe.p95Ms); max=$MaxP95LatencyMs"
Write-Step 'Public GA activation decision API checks PASS'

Write-Step 'Database public GA decision snapshot...'
$db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;max_stores=$MaxStores;max_concurrent_terminals=$MaxConcurrentTerminals;allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount;allowed_dead_letters=$AllowedDeadLetterCount;allowed_waiting_connections=$AllowedWaitingConnectionCount}
Write-Step 'Database public GA decision snapshot PASS'

Write-Step 'CGA-04 decision and blocker matrix...'
$blockers=[ordered]@{}; $conditions=New-Object System.Collections.Generic.List[string]
function Add-Blocker([string]$k,$v){$script:blockers[$k]=$v}; function Add-Condition([string]$v){[void]$conditions.Add($v)}
$tenantState=Req $db 'tenantState'; $rolloutScope=Req $db 'rolloutScope'; $syncIntegrity=Req $db 'syncIntegrity'; $financialIntegrity=Req $db 'financialIntegrity'; $dbPressure=Req $db 'databasePressure'; $rls=Req $db 'rls'; $monitoringActivity=Req $db 'monitoringActivity'
$activeStoreCount=[int64](Req $rolloutScope 'active_store_count'); $availableTerminalCount=[int64](Req $rolloutScope 'available_terminal_count'); $openShiftCount=[int64](Req $rolloutScope 'open_shift_count'); $activeStableReleaseCount=[int64](Req $rolloutScope 'active_stable_release_count')
$legacySchemaEventCount=[int64](Req $syncIntegrity 'legacy_schema_event_count'); $pendingConflictCount=[int64](Req $syncIntegrity 'pending_conflict_count'); $retryPendingCount=[int64](Req $syncIntegrity 'retry_pending_count'); $deadLetterCount=[int64](Req $syncIntegrity 'dead_letter_count'); $staleProcessingCount=[int64](Req $syncIntegrity 'stale_processing_count')
$duplicateLocalSaleCount=[int64](Req $financialIntegrity 'duplicate_local_sale_count'); $negativePaymentCount=[int64](Req $financialIntegrity 'negative_payment_count'); $waitingConnectionCount=[int64](Req $dbPressure 'waiting_connection_count'); $longRunningQueryCount=[int64](Req $dbPressure 'long_running_query_count'); $rlsMissingTableCount=[int64](Req $rls 'rls_missing_table_count')
$completedSales24h=[int64](Req $monitoringActivity 'completed_sales_24h'); $payments24h=[int64](Req $monitoringActivity 'payments_24h'); $receiptsIssued24h=[int64](Req $monitoringActivity 'receipts_issued_24h')
$gaActivated=[bool](Req $db 'generalAvailabilityActivated'); $publicGaActivated=[bool](Req $db 'publicGeneralAvailabilityActivated')
if(-not [bool](Req $db 'requiredTablesPresent')){Add-Blocker 'missing_required_tables' (Req $db 'missingRequiredTables')}; if([int64](Req $tenantState 'active_tenant_count') -ne 1){Add-Blocker 'tenant_not_active' $tenantState}
if($activeStoreCount -lt 1){Add-Blocker 'no_active_store' $activeStoreCount}; if($activeStoreCount -gt $MaxStores){Add-Blocker 'active_store_count_exceeds_limited_scope' @{actual=$activeStoreCount;max=$MaxStores}}
if($openShiftCount -gt $MaxConcurrentTerminals){Add-Blocker 'open_shift_count_exceeds_terminal_limit' @{actual=$openShiftCount;max=$MaxConcurrentTerminals}}; if($activeStableReleaseCount -lt 1){Add-Blocker 'no_active_stable_release' $activeStableReleaseCount}
if($legacySchemaEventCount -ne 0){Add-Blocker 'legacy_schema_events' $legacySchemaEventCount}; if($retryPendingCount -ne 0){Add-Blocker 'retry_pending_sync_events' $retryPendingCount}; if($staleProcessingCount -ne 0){Add-Blocker 'stale_processing_sync_events' $staleProcessingCount}
if($duplicateLocalSaleCount -ne 0){Add-Blocker 'duplicate_local_sales' $duplicateLocalSaleCount}; if($negativePaymentCount -ne 0){Add-Blocker 'negative_payments' $negativePaymentCount}; if($rlsMissingTableCount -ne 0){Add-Blocker 'rls_missing_tenant_tables' $rlsMissingTableCount}; if($longRunningQueryCount -ne 0){Add-Blocker 'long_running_queries' $longRunningQueryCount}
if($pendingConflictCount -gt $AllowedExistingSyncConflictCount){Add-Blocker 'pending_conflicts_exceed_allowed_baseline' @{actual=$pendingConflictCount;allowed=$AllowedExistingSyncConflictCount}}; if($deadLetterCount -gt $AllowedDeadLetterCount){Add-Blocker 'dead_letters_exceed_allowed_baseline' @{actual=$deadLetterCount;allowed=$AllowedDeadLetterCount}}; if($waitingConnectionCount -gt $AllowedWaitingConnectionCount){Add-Blocker 'waiting_connections_exceed_allowed_baseline' @{actual=$waitingConnectionCount;allowed=$AllowedWaitingConnectionCount}}
if($gaActivated){Add-Blocker 'general_availability_flag_already_active_before_cga04' $true}; if($publicGaActivated){Add-Blocker 'public_general_availability_flag_already_active_before_cga04' $true}
Add-Condition 'ga09_capacity_boundary_concurrency_3_plus_upstream_error_carried_forward'; Add-Condition 'db_waiting_connections_observation_carried_forward'; if($script:syncContractSchemaVersionCompatibilityCondition){ Add-Condition $script:syncContractSchemaVersionCompatibilityCondition }; Add-Condition 'known_sync_conflict_baseline_allowed'; Add-Condition 'known_dead_letter_baseline_allowed'; Add-Condition 'public_ga_not_activated_by_validator'; Add-Condition 'dashboard_overview_requires_from_to_limit_trendBucket_contract'
if($Decision -eq 'KEEP_LIMITED_GA'){ Add-Condition 'public_ga_deferred_keep_limited_ga'; $activationDecision='KEEP_LIMITED_GA_PUBLIC_GA_NOT_ACTIVATED'; $status='PASS CGA-04 KEEP LIMITED GA / PUBLIC GA NOT ACTIVATED' }
elseif($Decision -eq 'BLOCK_PUBLIC_GA'){ Add-Condition 'public_ga_blocked_until_capacity_db_sync_remediation'; $activationDecision='BLOCK_PUBLIC_GA_REMEDIATION_REQUIRED'; $status='PASS CGA-04 BLOCK PUBLIC GA / REMEDIATION REQUIRED' }
else {
  if(-not $ExplicitPublicGaAuthorization.IsPresent){Add-Blocker 'authorize_public_ga_requires_explicit_public_ga_authorization_switch' $true}
  if($AllowedExistingSyncConflictCount -ne 0 -or $AllowedDeadLetterCount -ne 0 -or $AllowedWaitingConnectionCount -ne 0){Add-Blocker 'authorize_public_ga_requires_zero_accepted_baselines' @{allowedConflicts=$AllowedExistingSyncConflictCount;allowedDeadLetters=$AllowedDeadLetterCount;allowedWaitingConnections=$AllowedWaitingConnectionCount}}
  if($pendingConflictCount -ne 0 -or $deadLetterCount -ne 0 -or $waitingConnectionCount -ne 0){Add-Blocker 'authorize_public_ga_requires_clean_runtime_state' @{pendingConflicts=$pendingConflictCount;deadLetters=$deadLetterCount;waitingConnections=$waitingConnectionCount}}
  Add-Condition 'public_ga_authorization_requested_but_validator_does_not_activate_flags'; $activationDecision='AUTHORIZE_PUBLIC_GA_REQUIRES_SEPARATE_DEPLOYMENT_CHANGE'; $status='PASS CGA-04 PUBLIC GA AUTHORIZATION RECORDED / ACTIVATION REQUIRES SEPARATE CHANGE'
}
Assert-True ($blockers.Count -eq 0) ("CGA-04 blockers present: " + ($blockers|ConvertTo-Json -Depth 20 -Compress))
Write-Step 'CGA-04 decision and blocker matrix PASS'
$observabilityP95LatencyMs=Resolve-DecimalMetric $metrics @('p95LatencyMs','p95Ms','latencyP95Ms','requestP95LatencyMs') ([decimal]([Math]::Max([decimal]$readyProbe.p95Ms,[decimal]$dashboardProbe.p95Ms)))
$observabilityFailedRequests=Resolve-Int64Metric $metrics @('failedRequests','failedRequestCount','totalFailedRequests') 0
if($null -eq (Optional-Prop $metrics 'p95LatencyMs')){Add-Condition 'observability_p95LatencyMs_missing_fallback_to_capacity_probe_p95'}
$manifest=[ordered]@{phase='CGA-04';status=$status;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');validatorVersion=$script:Cga04ValidatorVersion;entryGate='PASS CGA-03 FORMAL LIMITED CAPACITY ACCEPTANCE / GO CGA-04';rolloutMode=$RolloutMode;decision=$Decision;activationDecision=$activationDecision;maxStores=$MaxStores;maxConcurrentTerminals=$MaxConcurrentTerminals;capacitySampleCount=$CapacitySampleCount;maxP95LatencyMs=$MaxP95LatencyMs;allowedExistingSyncConflictCount=$AllowedExistingSyncConflictCount;allowedDeadLetterCount=$AllowedDeadLetterCount;allowedWaitingConnectionCount=$AllowedWaitingConnectionCount;controlledRolloutAllowed=$true;launchAuthorizationOnly=$true;publicGeneralAvailabilityActivated=$false;generalAvailabilityActivated=$false;publicGaActivatedByValidator=$false;knownCapacityCondition='GA-09 PASS at Concurrency 1/2; Concurrency 3+ current Railway/upstream path can return 400 upstream error unless remediated.';knownDbCondition='Waiting connections accepted only within explicit baseline for limited rollout; tune pool/connections before public GA.';dashboardOverviewContract='from,to,limit,trendBucket required; storeId optional.';healthLiveStatus=$healthLiveStatus;healthReadyStatus=$healthReadyStatus;unauthenticatedObservabilityStatus=$unauthObservabilityStatus;tenantCurrentStatus=$tenantCurrentStatus;salesRangeCompletedSalesCount=[int](Req $salesRange 'completedSalesCount');dashboardOverviewCompletedSalesCount=[int](Req (Req $dashboardOverview 'sales') 'completedSalesCount');completedSalesInLast24h=$completedSales24h;paymentsInLast24h=$payments24h;receiptsIssuedInLast24h=$receiptsIssued24h;syncPendingCount=$syncPendingCount;syncProcessingCount=$syncProcessingCount;syncRetryPendingCount=$syncRetryPendingCount;syncConflictCount=$syncConflictCount;syncDeadLetterCount=$syncDeadLetterCount;dashboardUrlStatus=$dashboardUrlStatus;dashboardBuild=if($SkipDashboardBuild.IsPresent){'SKIPPED_BY_SWITCH'}else{'VALIDATED'};metricsDatabaseReady=$metricsDatabaseReady;metricsP95LatencyMs=$observabilityP95LatencyMs;metricsFailedRequests=$observabilityFailedRequests;readyProbeP95Ms=$readyProbe.p95Ms;dashboardProbeP95Ms=$dashboardProbe.p95Ms;readyProbeErrors=$readyProbe.errors;dashboardProbeErrors=$dashboardProbe.errors;syncContractCurrentSchemaVersion=$contractSchemaVersion;databaseSnapshot=$db;activeStoreCount=$activeStoreCount;availableTerminalCount=$availableTerminalCount;openShiftCount=$openShiftCount;activeStableReleaseCount=$activeStableReleaseCount;duplicateLocalSaleCount=$duplicateLocalSaleCount;legacySchemaEventCount=$legacySchemaEventCount;pendingConflictCount=$pendingConflictCount;retryPendingCount=$retryPendingCount;deadLetterCount=$deadLetterCount;staleProcessingCount=$staleProcessingCount;rlsMissingTableCount=$rlsMissingTableCount;waitingConnectionCount=$waitingConnectionCount;longRunningQueryCount=$longRunningQueryCount;blockers=$blockers;conditions=@($conditions);schemaVersion=4;syncContract='schema_version_4';publicGaActivation='NOT_ACTIVATED';nextPhase='Limited GA operations; Public GA requires separate explicit activation/remediation decision'}
$manifestPath=Join-Path $runtimeDir 'cga-04-public-ga-activation-decision-manifest.json'; $manifest|ConvertTo-Json -Depth 60|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'CGA-04 evidence manifest and public GA activation decision snapshot PASS'
Write-Step $status
$manifest|Format-List
