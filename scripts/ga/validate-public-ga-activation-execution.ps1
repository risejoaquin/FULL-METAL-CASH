param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [Parameter(Mandatory=$true)][switch]$ExecuteActivation,
    [Parameter(Mandatory=$true)][ValidateSet('ACTIVATE_PUBLIC_GA')][string]$ConfirmationPhrase,
    [int]$AllowedWaitingConnectionCount = 12,
    [int]$AllowedNegativeStockItemCount = 0,
    [int]$PublicGaReadinessConcurrency = 3,
    [int]$ConcurrencyProbeRequests = 6,
    [int]$MaxReadinessP95Ms = 1200,
    [switch]$WpfVisualConfirmed,
    [switch]$SkipDashboardBuild,
    [switch]$SkipActivationDecisionRevalidation,
    [switch]$DisableAutoRollback
)
$ErrorActionPreference='Stop'
$script:ValidatorVersion='PUBLIC-GA-ACTIVATION-EXECUTION.1.1-safe-psql-guards'
function Write-Step([string]$m){Write-Host "[PUBLIC-GA-ACTIVATION-EXECUTION] $m"}
function Assert-True($c,[string]$m){if(-not $c){throw $m}}
function Invoke-DbRaw([string]$SqlPath,[hashtable]$Vars){
  $file=(Resolve-Path $SqlPath).Path
  $cmd=Get-Command psql -ErrorAction SilentlyContinue
  if($cmd){
    $args=@($DatabaseUrl,'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
    foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")}
    $args += @('-f',$file)
    $global:LASTEXITCODE=0;$out=& $cmd.Source @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0
    if($e -ne 0){throw "Database SQL failed via psql: $(Split-Path -Leaf $file)"}
    return @($out | ForEach-Object {[string]$_})
  }
  $docker=Get-Command docker -ErrorAction SilentlyContinue
  Assert-True ($null -ne $docker) 'Neither psql nor docker is available.'
  $dir=(Resolve-Path (Split-Path -Parent $SqlPath)).Path;$leaf=Split-Path -Leaf $SqlPath
  $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${dir}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
  foreach($k in $Vars.Keys){$args += @('-v',"$k=$($Vars[$k])")}
  $args += @('-f',"/sql/$leaf")
  $global:LASTEXITCODE=0;$out=& $docker.Source @args;$e=$LASTEXITCODE;$global:LASTEXITCODE=0
  if($e -ne 0){throw "Database SQL failed via docker: $leaf"}
  return @($out | ForEach-Object {[string]$_})
}
function Invoke-DbJson([string]$SqlPath,[hashtable]$Vars,[string]$Marker){
  $raw=(Invoke-DbRaw $SqlPath $Vars) -join "`n";$idx=$raw.LastIndexOf($Marker)
  if($idx -lt 0){throw "No JSON marker $Marker returned by $(Split-Path -Leaf $SqlPath). Raw=$raw"}
  return ($raw.Substring($idx+$Marker.Length).Trim() | ConvertFrom-Json)
}
function Invoke-ConcurrencyProbe([string]$Path,[int]$Concurrency,[int]$Requests,[int]$TimeoutSec=20){
  $uri="$script:base$Path";$curl=Get-Command curl.exe -ErrorAction SilentlyContinue
  Assert-True ($null -ne $curl) 'curl.exe is required for latency probes.'
  $jobs=@();$results=@()
  for($i=0;$i -lt $Requests;$i++){
    while(@($jobs|Where-Object {$_.State -eq 'Running'}).Count -ge $Concurrency){
      [void](Wait-Job -Job @($jobs) -Any -Timeout 1)
      foreach($j in @($jobs|Where-Object {$_.State -ne 'Running'})){
        $received=@(Receive-Job -Job $j -ErrorAction SilentlyContinue);Remove-Job $j -Force -ErrorAction SilentlyContinue
        $jobs=@($jobs|Where-Object {$_.Id -ne $j.Id});foreach($item in $received){if($null -ne $item){$results+=$item}}
      }
    }
    $jobs += Start-Job -ScriptBlock {
      param($curlPath,$u,$timeout)
      $status=0;$ms=0;$err=''
      try{$format='__SOLIDPOS_METRIC__ %{http_code} %{time_total}';$output=& $curlPath '-sS' '-o' 'NUL' '--max-time' ([string]$timeout) '-w' $format $u 2>&1;$exitCode=$LASTEXITCODE;$line=(@($output|%{[string]$_})|?{$_ -like '__SOLIDPOS_METRIC__*'}|Select-Object -Last 1);if($line -match '^__SOLIDPOS_METRIC__\s+(\d{3})\s+([0-9.]+)$'){$status=[int]$Matches[1];$seconds=[double]::Parse($Matches[2],[Globalization.CultureInfo]::InvariantCulture);$ms=[int][Math]::Round($seconds*1000)}else{$err=(@($output|%{[string]$_}) -join ' | ')};if($exitCode -ne 0 -and -not $err){$err="curl exit code $exitCode"}}catch{$err=$_.Exception.Message};[pscustomobject]@{status=$status;ms=$ms;error=$err}
    } -ArgumentList @($curl.Source,$uri,$TimeoutSec)
  }
  while(@($jobs).Count -gt 0){[void](Wait-Job -Job @($jobs) -Any -Timeout 1);foreach($j in @($jobs|Where-Object {$_.State -ne 'Running'})){$received=@(Receive-Job $j -ErrorAction SilentlyContinue);Remove-Job $j -Force -ErrorAction SilentlyContinue;$jobs=@($jobs|Where-Object {$_.Id -ne $j.Id});foreach($item in $received){if($null -ne $item){$results+=$item}}}}
  $success=@($results|?{[int]$_.status -ge 200 -and [int]$_.status -lt 300});$lat=@($success|%{[int]$_.ms}|Sort-Object);$p95=0;if($lat.Count -gt 0){$idx=[Math]::Ceiling($lat.Count*.95)-1;if($idx -lt 0){$idx=0};if($idx -ge $lat.Count){$idx=$lat.Count-1};$p95=[int]$lat[$idx]}
  return [pscustomobject]@{path=$Path;concurrency=$Concurrency;requests=$Requests;successCount=$success.Count;failureCount=($results.Count-$success.Count);p95Ms=$p95}
}
function Invoke-Rollback([string]$Reason){
  Write-Step "ROLLBACK requested: $Reason"
  [void](Invoke-DbRaw $rollbackSql @{tenant_id=$TenantId;confirmation_phrase='ROLLBACK_PUBLIC_GA';rollback_reason=$Reason})
  $rolled=Invoke-DbJson $stateSql @{tenant_id=$TenantId} 'PUBLIC_GA_ACTIVATION_STATE_JSON:'
  Assert-True (-not [bool]$rolled.generalAvailabilityActivated) 'Rollback verification failed: generalAvailabilityActivated remains true.'
  Assert-True (-not [bool]$rolled.publicGeneralAvailabilityActivated) 'Rollback verification failed: publicGeneralAvailabilityActivated remains true.'
  Write-Step 'ROLLBACK PASS / PUBLIC GA NOT ACTIVATED'
}

$script:base=$BaseUrl.TrimEnd('/');$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$decisionValidator=Join-Path $scriptRoot 'validate-public-ga-activation-decision.ps1';$stateSql=Join-Path $scriptRoot 'public-ga-activation-state-check.sql';$executeSql=Join-Path $scriptRoot 'public-ga-activation-execute.sql';$rollbackSql=Join-Path $scriptRoot 'public-ga-activation-rollback.sql'
$runtimeDir=Join-Path $repo '.runtime\public-ga-activation-execution';New-Item -ItemType Directory -Force -Path $runtimeDir|Out-Null
Write-Step "Validator version $script:ValidatorVersion"
Assert-True $ExecuteActivation.IsPresent 'Execution switch -ExecuteActivation is mandatory.'
Assert-True ($ConfirmationPhrase -eq 'ACTIVATE_PUBLIC_GA') 'Exact confirmation phrase ACTIVATE_PUBLIC_GA is mandatory.'
foreach($f in @($decisionValidator,$stateSql,$executeSql,$rollbackSql)){Assert-True (Test-Path $f) "Required execution asset missing: $f"}

Write-Step 'Preflight activation decision...'
if(-not $SkipActivationDecisionRevalidation.IsPresent){
  $decisionArgs=@{BaseUrl=$BaseUrl;DashboardUrl=$DashboardUrl;TenantId=$TenantId;Email=$Email;Password=$Password;DatabaseUrl=$DatabaseUrl;ConflictDecision='FORMAL_ARCHIVE';DeadLetterDecision='FORMAL_ARCHIVE';CapacityDecision='CAPACITY_GATE_PASSED';ActivationDecision='APPROVE_PUBLIC_GA_ACTIVATION';ExecutionDecision='KEEP_NOT_ACTIVATED';AllowedWaitingConnectionCount=$AllowedWaitingConnectionCount;AllowedNegativeStockItemCount=$AllowedNegativeStockItemCount;PublicGaReadinessConcurrency=$PublicGaReadinessConcurrency;ConcurrencyProbeRequests=$ConcurrencyProbeRequests;MaxReadinessP95Ms=$MaxReadinessP95Ms;WpfVisualConfirmed=$WpfVisualConfirmed.IsPresent;SkipDashboardBuild=$SkipDashboardBuild.IsPresent;SkipPublicGaReadinessReviewRevalidation=$true}
  & $decisionValidator @decisionArgs
  if($LASTEXITCODE -ne 0){throw 'Public GA Activation Decision prerequisite failed.'}
}else{Write-Step 'Activation decision revalidation skipped by explicit switch; reviewed PASS logs are required.'}
Write-Step 'Preflight activation decision PASS'

$before=Invoke-DbJson $stateSql @{tenant_id=$TenantId} 'PUBLIC_GA_ACTIVATION_STATE_JSON:'
Assert-True ([bool]$before.configPresent) 'tenant_configs row missing.'
Assert-True (-not [bool]$before.generalAvailabilityActivated) 'Preflight blocked: General Availability already activated.'
Assert-True (-not [bool]$before.publicGeneralAvailabilityActivated) 'Preflight blocked: Public GA already activated.'
Assert-True ([int]$before.negativeStockCount -le $AllowedNegativeStockItemCount) 'Preflight blocked: negative stock above allowed baseline.'
Assert-True ([int]$before.waitingConnectionCount -le $AllowedWaitingConnectionCount) 'Preflight blocked: waiting connections above allowed baseline.'
Assert-True ([int]$before.longRunningQueryCount -eq 0) 'Preflight blocked: long-running queries present.'
$liveBefore=Invoke-ConcurrencyProbe '/health/live' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests;$readyBefore=Invoke-ConcurrencyProbe '/health/ready' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
Assert-True ($liveBefore.successCount -eq $ConcurrencyProbeRequests -and $liveBefore.p95Ms -le $MaxReadinessP95Ms) "Preflight /health/live capacity failed: $($liveBefore|ConvertTo-Json -Compress)"
Assert-True ($readyBefore.successCount -eq $ConcurrencyProbeRequests -and $readyBefore.p95Ms -le $MaxReadinessP95Ms) "Preflight /health/ready capacity failed: $($readyBefore|ConvertTo-Json -Compress)"
Write-Step 'Immediate preflight PASS'

$activationApplied=$false
try{
  Write-Step 'EXECUTING PUBLIC GA ACTIVATION transaction...'
  [void](Invoke-DbRaw $executeSql @{tenant_id=$TenantId;confirmation_phrase=$ConfirmationPhrase})
  $activationApplied=$true
  $after=Invoke-DbJson $stateSql @{tenant_id=$TenantId} 'PUBLIC_GA_ACTIVATION_STATE_JSON:'
  Assert-True ([bool]$after.generalAvailabilityActivated) 'Postflight failed: generalAvailabilityActivated is false.'
  Assert-True ([bool]$after.publicGeneralAvailabilityActivated) 'Postflight failed: publicGeneralAvailabilityActivated is false.'
  Assert-True ([string]$after.publicGaActivation -eq 'ACTIVATED') 'Postflight failed: publicGaActivation is not ACTIVATED.'
  Assert-True ([string]$after.rolloutStage -eq 'public_ga') 'Postflight failed: rolloutStage is not public_ga.'
  $liveAfter=Invoke-ConcurrencyProbe '/health/live' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests;$readyAfter=Invoke-ConcurrencyProbe '/health/ready' $PublicGaReadinessConcurrency $ConcurrencyProbeRequests
  Assert-True ($liveAfter.successCount -eq $ConcurrencyProbeRequests -and $liveAfter.p95Ms -le $MaxReadinessP95Ms) "Postflight /health/live capacity failed: $($liveAfter|ConvertTo-Json -Compress)"
  Assert-True ($readyAfter.successCount -eq $ConcurrencyProbeRequests -and $readyAfter.p95Ms -le $MaxReadinessP95Ms) "Postflight /health/ready capacity failed: $($readyAfter|ConvertTo-Json -Compress)"
  Assert-True ([int]$after.negativeStockCount -le $AllowedNegativeStockItemCount) 'Postflight failed: negative stock regression.'
  Assert-True ([int]$after.waitingConnectionCount -le $AllowedWaitingConnectionCount) 'Postflight failed: waiting connections above baseline.'
  Assert-True ([int]$after.longRunningQueryCount -eq 0) 'Postflight failed: long-running queries present.'
  $manifest=[ordered]@{phase='PUBLIC-GA-ACTIVATION-EXECUTION';status='PASS PUBLIC GA ACTIVATION EXECUTION / PUBLIC GA ACTIVATED / POSTFLIGHT PASS';validatorVersion=$script:ValidatorVersion;tenantId=$TenantId;baseUrl=$BaseUrl;generatedAt=(Get-Date).ToUniversalTime().ToString('o');activationExecuted=$true;generalAvailabilityActivated=$true;publicGeneralAvailabilityActivated=$true;publicGaActivation='ACTIVATED';rolloutStage='public_ga';beforeState=$before;afterState=$after;healthLiveBefore=$liveBefore;healthReadyBefore=$readyBefore;healthLiveAfter=$liveAfter;healthReadyAfter=$readyAfter;schemaVersion=4;syncContract='schema_version_4';autoRollbackEnabled=(-not $DisableAutoRollback.IsPresent);blockerCount=0;nextPhase='POST_PUBLIC_GA_ACTIVATION_VALIDATION_REQUIRED'}
  $path=Join-Path $runtimeDir ("public-ga-activation-execution-"+(Get-Date).ToString('yyyy-MM-dd-HHmmss')+'.json');$manifest|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $path
  Write-Step 'PASS PUBLIC GA ACTIVATION EXECUTION / PUBLIC GA ACTIVATED / POSTFLIGHT PASS'
  $manifest|Format-List
}catch{
  $failure=$_.Exception.Message
  if($activationApplied -and -not $DisableAutoRollback.IsPresent){
    try{Invoke-Rollback $failure}catch{throw "Activation postflight failed: $failure. AUTOMATIC ROLLBACK ALSO FAILED: $($_.Exception.Message)"}
    throw "Activation postflight failed and automatic rollback completed. Public GA returned to NOT_ACTIVATED. Cause: $failure"
  }
  throw
}
