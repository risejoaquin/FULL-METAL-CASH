param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [string]$DashboardUrl='',
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [ValidateSet('CLOSE_SOLIDPOS_V1')][string]$ClosureDecision='CLOSE_SOLIDPOS_V1',
 [ValidateSet('KEEP_PUBLIC_GA_ACTIVE')][string]$ProductionDecision='KEEP_PUBLIC_GA_ACTIVE',
 [int]$MaxStores=2,
 [int]$AllowedExistingSyncConflictCount=3,
 [int]$AllowedDeadLetterCount=1,
 [int]$AllowedNegativeStockItemCount=0,
 [int]$AllowedOpenShiftCount=0,
 [int]$AllowedCashDifferenceLast24hCount=0,
 [int]$AllowedWaitingConnectionCount=12,
 [int]$MinCompletedSalesInLast24h=6,
 [int]$MinPaymentsInLast24h=6,
 [int]$MinReceiptsIssuedInLast24h=3,
 [int]$MinAuditEventsInLast24h=1,
 [int]$PublicGaReadinessConcurrency=3,
 [int]$ConcurrencyProbeRequests=6,
 [int]$MaxReadinessP95Ms=1200,
 [switch]$WpfVisualConfirmed,
 [switch]$SkipDashboardBuild,
 [switch]$SkipBuildAndTests,
 [switch]$SkipBurnInRevalidation
)
$ErrorActionPreference='Stop'
$script:ValidatorVersion='FINAL-PUBLIC-GA-PRODUCTION-CLOSURE.1.0-v1-production-closure'
function Write-Step([string]$m){Write-Host "[FINAL-PUBLIC-GA-CLOSURE] $m"}
function Assert-True($c,[string]$m){if(-not $c){throw $m}}
function Invoke-DbRaw([string]$SqlPath,[hashtable]$Vars){
 $file=(Resolve-Path $SqlPath).Path;$cmd=Get-Command psql -ErrorAction SilentlyContinue
 if($cmd){$args=@($DatabaseUrl,'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$args+=@('-v',"$k=$($Vars[$k])")};$args+=@('-f',$file);$global:LASTEXITCODE=0;$out=& $cmd.Source @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "Database SQL failed via psql: $(Split-Path -Leaf $file)"};return @($out|%{[string]$_})}
 $docker=Get-Command docker -ErrorAction SilentlyContinue;Assert-True ($null -ne $docker) 'Neither psql nor docker is available.'
 $dir=(Resolve-Path (Split-Path -Parent $SqlPath)).Path;$leaf=Split-Path -Leaf $SqlPath;$args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${dir}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$args+=@('-v',"$k=$($Vars[$k])")};$args+=@('-f',"/sql/$leaf");$global:LASTEXITCODE=0;$out=& $docker.Source @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "Database SQL failed via docker: $leaf"};return @($out|%{[string]$_})
}
function Invoke-DbJson([string]$SqlPath,[hashtable]$Vars,[string]$Marker){$raw=(Invoke-DbRaw $SqlPath $Vars)-join"`n";$idx=$raw.LastIndexOf($Marker);if($idx -lt 0){throw "No JSON marker $Marker returned."};return ($raw.Substring($idx+$Marker.Length).Trim()|ConvertFrom-Json)}
function Invoke-ConcurrencyProbe([string]$Path,[int]$Concurrency,[int]$Requests,[int]$TimeoutSec=20){
  $uri="$script:base$Path";$curl=Get-Command curl.exe -ErrorAction SilentlyContinue;Assert-True ($null -ne $curl) 'curl.exe is required for latency probes.';$jobs=@();$results=@()
  for($i=0;$i -lt $Requests;$i++){
    while(@($jobs|Where-Object {$_.State -eq 'Running'}).Count -ge $Concurrency){[void](Wait-Job -Job @($jobs) -Any -Timeout 1);foreach($j in @($jobs|Where-Object {$_.State -ne 'Running'})){$received=@(Receive-Job -Job $j -ErrorAction SilentlyContinue);Remove-Job $j -Force -ErrorAction SilentlyContinue;$jobs=@($jobs|Where-Object {$_.Id -ne $j.Id});foreach($item in $received){if($null -ne $item){$results+=$item}}}}
    $jobs += Start-Job -ScriptBlock {param($curlPath,$u,$timeout);$status=0;$ms=0;$err='';try{$format='__SOLIDPOS_METRIC__ %{http_code} %{time_total}';$output=& $curlPath '-sS' '-o' 'NUL' '--max-time' ([string]$timeout) '-w' $format $u 2>&1;$exitCode=$LASTEXITCODE;$line=(@($output|%{[string]$_})|?{$_ -like '__SOLIDPOS_METRIC__*'}|Select-Object -Last 1);if($line -match '^__SOLIDPOS_METRIC__\s+(\d{3})\s+([0-9.]+)$'){$status=[int]$Matches[1];$seconds=[double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture);$ms=[int][Math]::Round($seconds*1000)}else{$err=(@($output|%{[string]$_}) -join ' | ')};if($exitCode -ne 0 -and -not $err){$err="curl exit code $exitCode"}}catch{$err=$_.Exception.Message};[pscustomobject]@{status=$status;ms=$ms;error=$err}} -ArgumentList @($curl.Source,$uri,$TimeoutSec)
  }
  while(@($jobs).Count -gt 0){[void](Wait-Job -Job @($jobs) -Any -Timeout 1);foreach($j in @($jobs|Where-Object {$_.State -ne 'Running'})){$received=@(Receive-Job $j -ErrorAction SilentlyContinue);Remove-Job $j -Force -ErrorAction SilentlyContinue;$jobs=@($jobs|Where-Object {$_.Id -ne $j.Id});foreach($item in $received){if($null -ne $item){$results+=$item}}}}
  $success=@($results|?{[int]$_.status -ge 200 -and [int]$_.status -lt 300});$lat=@($success|%{[int]$_.ms}|Sort-Object);$p95=0;if($lat.Count -gt 0){$idx=[Math]::Ceiling($lat.Count*.95)-1;if($idx -lt 0){$idx=0};if($idx -ge $lat.Count){$idx=$lat.Count-1};$p95=[int]$lat[$idx]};return [pscustomobject]@{path=$Path;concurrency=$Concurrency;requests=$Requests;successCount=$success.Count;failureCount=($results.Count-$success.Count);p95Ms=$p95;errors=@($results|?{[int]$_.status -lt 200 -or [int]$_.status -ge 300}|%{[string]$_.error}|?{$_}|Select-Object -Unique)}
}
function Get-Blockers($db){$b=@();if(-not[bool]$db.generalAvailabilityActivated){$b+='ga_not_activated'};if(-not[bool]$db.publicGeneralAvailabilityActivated){$b+='public_ga_not_activated'};if([string]$db.publicGaActivation -ne 'ACTIVATED'){$b+='public_ga_activation_state_invalid'};if([string]$db.rolloutStage -ne 'public_ga'){$b+='rollout_stage_not_public_ga'};if(-not[bool]$db.requiredTablesPresent){$b+='required_tables_missing'};if([int]$db.negativeStock.negative_stock_count -gt $AllowedNegativeStockItemCount){$b+='negative_stock'};if([int]$db.databasePressure.waiting_connection_count -gt $AllowedWaitingConnectionCount){$b+='db_waiting_connections'};if([int]$db.databasePressure.long_running_query_count -ne 0){$b+='long_running_queries'};if([int]$db.rls.rls_missing_table_count -ne 0){$b+='rls_missing'};if([int]$db.rolloutScope.open_shift_count -gt $AllowedOpenShiftCount){$b+='open_shifts'};if([int]$db.rolloutScope.cash_difference_last_24h_count -gt $AllowedCashDifferenceLast24hCount){$b+='cash_difference'};if([int]$db.syncIntegrity.pending_count -ne 0 -or [int]$db.syncIntegrity.processing_count -ne 0 -or [int]$db.syncIntegrity.retry_pending_count -ne 0){$b+='sync_queue_not_clean'};if([int]$db.syncIntegrity.conflict_count -gt $AllowedExistingSyncConflictCount){$b+='sync_conflicts'};if([int]$db.syncIntegrity.dead_letter_count -gt $AllowedDeadLetterCount){$b+='dead_letters'};if([int]$db.monitoringActivity.completed_sales_24h -lt $MinCompletedSalesInLast24h){$b+='sales_activity'};if([int]$db.monitoringActivity.payments_24h -lt $MinPaymentsInLast24h){$b+='payments_activity'};if([int]$db.monitoringActivity.receipts_issued_24h -lt $MinReceiptsIssuedInLast24h){$b+='receipt_activity'};if([int]$db.monitoringActivity.audit_events_24h -lt $MinAuditEventsInLast24h){$b+='audit_activity'};if([int]$db.financialIntegrity.duplicate_local_sale_count -ne 0 -or [int]$db.financialIntegrity.negative_payment_count -ne 0){$b+='financial_integrity'};return @($b)}
$script:base=$BaseUrl.TrimEnd('/');$root=(Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) '..\..')).Path;$sql=Join-Path (Split-Path -Parent $PSCommandPath) 'final-public-ga-production-closure-check.sql';$runtime=Join-Path $root '.runtime\final-public-ga-production-closure';New-Item -ItemType Directory -Force $runtime|Out-Null
Write-Step "Validator version $script:ValidatorVersion"
Assert-True($ClosureDecision -eq 'CLOSE_SOLIDPOS_V1') 'ClosureDecision must be CLOSE_SOLIDPOS_V1.';Assert-True($ProductionDecision -eq 'KEEP_PUBLIC_GA_ACTIVE') 'ProductionDecision must keep Public GA active.'
if($SkipBurnInRevalidation){Write-Step 'Burn-in prerequisite revalidation skipped; reviewed PASS production logs are required.'}else{Write-Step 'Burn-in prerequisite expected from reviewed PASS evidence; validator performs an independent final production verification.'}
if(-not$SkipBuildAndTests){Write-Step 'Local build/test/secret/WPF guardrails...';dotnet build (Join-Path $root 'solidpos-platform.sln');if($LASTEXITCODE -ne 0){throw'Build failed.'};dotnet test (Join-Path $root 'solidpos-platform.sln') --no-build;if($LASTEXITCODE -ne 0){throw'Tests failed.'};& (Join-Path $root 'scripts\security\scan-local-secrets.ps1') -Root $root;if(-not$WpfVisualConfirmed){throw'WPF visual confirmation required.'};Write-Step 'Local build/test/secret/WPF guardrails PASS'}
Write-Step 'Final Public GA capacity verification...';$live=Invoke-ConcurrencyProbe '/health/live' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests;$ready=Invoke-ConcurrencyProbe '/health/ready' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests;Assert-True($live.successCount -eq $ConcurrencyProbeRequests -and $live.p95Ms -le $MaxReadinessP95Ms)('live capacity failed: '+($live|ConvertTo-Json -Compress));Assert-True($ready.successCount -eq $ConcurrencyProbeRequests -and $ready.p95Ms -le $MaxReadinessP95Ms)('ready capacity failed: '+($ready|ConvertTo-Json -Compress));Write-Step 'Final Public GA capacity verification PASS'
Write-Step 'Final production database snapshot...';$db=Invoke-DbJson $sql @{tenant_id=$TenantId;max_stores=$MaxStores;allowed_existing_sync_conflicts=$AllowedExistingSyncConflictCount;allowed_dead_letters=$AllowedDeadLetterCount;allowed_waiting_connections=$AllowedWaitingConnectionCount} 'FINAL_PUBLIC_GA_CLOSURE_JSON:';$blockers=Get-Blockers $db;Assert-True($blockers.Count -eq 0)('final closure blockers: '+($blockers -join ','));Write-Step 'Final production blocker matrix PASS'
$m=[ordered]@{phase='FINAL-PUBLIC-GA-PRODUCTION-CLOSURE';status='PASS FINAL PUBLIC GA PRODUCTION CLOSURE / SOLIDPOS V1 PRODUCTION BASELINE CLOSED / PUBLIC GA ACTIVE';validatorVersion=$script:ValidatorVersion;tenantId=$TenantId;baseUrl=$BaseUrl;dashboardUrl=$DashboardUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');closureDecision=$ClosureDecision;productionDecision=$ProductionDecision;generalAvailabilityActivated=[bool]$db.generalAvailabilityActivated;publicGeneralAvailabilityActivated=[bool]$db.publicGeneralAvailabilityActivated;publicGaActivation=[string]$db.publicGaActivation;rolloutStage=[string]$db.rolloutStage;publicGaActivatedAt=[string]$db.publicGaActivatedAt;schemaVersion=4;syncContract='schema_version_4';healthLiveConcurrencyProbe=$live;healthReadyConcurrencyProbe=$ready;databaseSnapshot=$db;databasePressureMetric='active_non_client_wait_event';blockerCount=0;blockers=@();softwareLifecycleState='PRODUCTION_GA_OPERATION_AND_MAINTENANCE';productBaseline='SOLIDPOS_V1_QSR_CURRENT_SCOPE_CLOSED';developmentCompletionPercent=100;nextPhase='SOFTWARE_LIFECYCLE_OPERATIONS'};$out=Join-Path $runtime ("final-public-ga-production-closure-"+(Get-Date).ToString('yyyy-MM-dd-HHmmss')+'.json');$m|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $out;Write-Step 'PASS FINAL PUBLIC GA PRODUCTION CLOSURE / SOLIDPOS V1 PRODUCTION BASELINE CLOSED / PUBLIC GA ACTIVE';$m|Format-List
