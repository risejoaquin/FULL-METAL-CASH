param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [int]$AllowedWaitingConnectionCount = 12,
    [int]$DiagnosticEscalationConnectionCount = 13,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
$script:ValidatorVersion='LGA-07.1-db-pressure-diagnostics-no-public-ga'
function Write-Step([string]$m){Write-Host "[LGA-07.1] $m"}
function Assert-True($c,[string]$m){if(-not $c){throw $m}}
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;& $c;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "$n failed with exit code $e"}}
function Get-HttpStatus([string]$Method,[string]$Uri,[hashtable]$Headers=@{},[int]$TimeoutSec=30){try{$r=Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -TimeoutSec $TimeoutSec -UseBasicParsing;return [int]$r.StatusCode}catch{if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
function Assert-DocumentContains([string]$Path,[string[]]$Terms){Assert-True (Test-Path $Path) "Required document missing: $Path";$c=(Get-Content -Raw $Path).ToLowerInvariant();foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"}}
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
    Assert-True ($null -ne $docker) 'Neither psql nor docker is available. Install PostgreSQL client tools or Docker Desktop.'
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
function Invoke-ApiLogin([string]$Base,[string]$Tenant,[string]$User,[string]$PlainPassword){
  $body=@{tenantId=$Tenant;email=$User;password=$PlainPassword}|ConvertTo-Json -Depth 8
  try{return Invoke-RestMethod -Method Post -Uri "$Base/api/v1/auth/login" -ContentType 'application/json' -Body $body -TimeoutSec 30}catch{throw "Login failed for LGA-07.1 diagnostics. $($_.Exception.Message)"}
}

$script:base=$BaseUrl.TrimEnd('/');$plainPassword=Convert-Secret $Password
$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sqlPath=Join-Path $scriptRoot 'lga-07-hotfix-07-1-db-pressure-diagnostics-check.sql'
$runtimeDir=Join-Path $repo '.runtime\lga-07-hotfix-07-1-db-pressure-diagnostics'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:ValidatorVersion"
Write-Step 'Repository/document HOTFIX LGA-07.1 guardrails...'
Assert-True (Test-Path (Join-Path $repo 'solidpos-platform.sln')) 'solidpos-platform.sln missing'
Assert-True (Test-Path $sqlPath) 'LGA-07.1 diagnostic SQL missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-07-hotfix-07-1-db-pressure-diagnostics.md') @('lga-07.1','db pressure','waiting connections','diagnostic only','public ga not activated','no baseline increase')
Write-Step 'Repository/document HOTFIX LGA-07.1 guardrails PASS'

Write-Step 'Local build/test/secret guardrails...'
Invoke-Checked 'dotnet build' { dotnet build (Join-Path $repo 'solidpos-platform.sln') --nologo }
Invoke-Checked 'dotnet test' { dotnet test (Join-Path $repo 'solidpos-platform.sln') --no-build --nologo }
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
if(Test-Path $secretScan){Invoke-Checked 'secret scan' { & $secretScan }}
if($SkipDashboardBuild.IsPresent){Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret guardrails PASS'

Write-Step 'Limited GA API reachability checks...'
$login=Invoke-ApiLogin $script:base $TenantId $Email $plainPassword
$accessToken=$login.accessToken
Assert-True (![string]::IsNullOrWhiteSpace($accessToken)) 'accessToken missing from login response'
$headers=@{Authorization="Bearer $accessToken"}
$live=Get-HttpStatus GET "$script:base/health/live"
$ready=Get-HttpStatus GET "$script:base/health/ready"
$inventory=Get-HttpStatus GET "$script:base/api/v1/inventory/stock" $headers
Assert-True ($live -eq 200) "/health/live must be 200; status=$live"
Assert-True ($ready -eq 200) "/health/ready must be 200; status=$ready"
Assert-True ($inventory -eq 200) "inventory stock endpoint must be 200; status=$inventory"
Write-Step 'Limited GA API reachability checks PASS'

Write-Step 'Database pressure diagnostics snapshot...'
$db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId} 'LGA071_DIAGNOSTIC_JSON:'
$pressure=$db.databasePressure
$waiting=[int64]$pressure.waiting_connection_count
$observed=[int64]$pressure.observed_connection_count
$active=[int64]$pressure.active_connection_count
$idle=[int64]$pressure.idle_connection_count
$idleTx=[int64]$pressure.idle_in_transaction_connection_count
$longRunning=[int64]$pressure.long_running_query_count
Write-Step "DB pressure: waiting=$waiting observed=$observed active=$active idle=$idle idleInTx=$idleTx longRunning=$longRunning"

$classification='WITHIN_LIMITED_GA_BASELINE'
$recommendedAction='Continue LGA-07 with AllowedWaitingConnectionCount 12 when waiting connections are <= 12.'
if($waiting -ge $DiagnosticEscalationConnectionCount){
  $classification='CAPACITY_UPGRADE_OR_DB_POOL_REMEDIATION_REQUIRED'
  $recommendedAction='Do not raise the baseline. Keep LGA-07 pending. Apply Railway Pro/scaling or tune DB/API connection pool, then rerun LGA-07.'
}

$manifest=[ordered]@{
  phase='LGA-07.1'
  status='PASS HOTFIX LGA-07.1 DB PRESSURE DIAGNOSTICS / LGA-07 REMAINS PENDING WHEN WAITING CONNECTIONS EXCEED BASELINE'
  validatorVersion=$script:ValidatorVersion
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  tenantId=$TenantId
  baseUrl=$BaseUrl
  dashboardUrl=$DashboardUrl
  allowedWaitingConnectionCount=$AllowedWaitingConnectionCount
  diagnosticEscalationConnectionCount=$DiagnosticEscalationConnectionCount
  waitingConnectionCount=$waiting
  observedConnectionCount=$observed
  activeConnectionCount=$active
  idleConnectionCount=$idle
  idleInTransactionConnectionCount=$idleTx
  longRunningQueryCount=$longRunning
  classification=$classification
  recommendedAction=$recommendedAction
  databaseDiagnostics=$db
  publicGaActivation='NOT_ACTIVATED'
  nextPhase=if($classification -eq 'CAPACITY_UPGRADE_OR_DB_POOL_REMEDIATION_REQUIRED'){'LGA-07 remains pending - execute capacity upgrade or DB pool remediation'}else{'Rerun LGA-07 continued monitoring validator'}
}
$manifestPath=Join-Path $runtimeDir ("lga-07-hotfix-07-1-db-pressure-diagnostics-" + (Get-Date).ToString('yyyy-MM-dd-HHmmss') + '.json')
$manifest|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $manifestPath
Write-Step 'Database pressure diagnostics snapshot PASS'
Write-Step $manifest.status
$manifest|Format-List
