param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [string]$DashboardUrl = '',
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [int]$AllowedNegativeStockItemCount = 0,
    [int]$AllowedWaitingConnectionCount = 12,
    [switch]$ApplyInventoryCorrection,
    [string]$CorrectionReason = 'LGA-07.4 controlled inventory adjustment contract alignment for negative stock regression',
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
$script:ValidatorVersion='LGA-07.4-inventory-adjustment-contract-alignment-no-public-ga'
function Write-Step([string]$m){Write-Host "[LGA-07.4] $m"}
function Assert-True($c,[string]$m){if(-not $c){throw $m}}
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;& $c;$e=$LASTEXITCODE;$global:LASTEXITCODE=0;if($e -ne 0){throw "$n failed with exit code $e"}}
function Req($o,[string]$n){Assert-True ($null -ne $o) "Object missing while checking property $n";$p=$o.PSObject.Properties[$n];Assert-True ($null -ne $p) "Required property missing: $n";return $p.Value}
function Optional-Prop($o,[string]$n){if($null -eq $o){return $null};$p=$o.PSObject.Properties[$n];if($null -eq $p){return $null};return $p.Value}
function Assert-DocumentContains([string]$Path,[string[]]$Terms){Assert-True (Test-Path $Path) "Required document missing: $Path";$c=(Get-Content -Raw $Path).ToLowerInvariant();foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"}}
function Get-HttpStatus([string]$Method,[string]$Uri,[hashtable]$Headers=@{},[int]$TimeoutSec=30){try{$r=Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -TimeoutSec $TimeoutSec -UseBasicParsing;return [int]$r.StatusCode}catch{if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{},[int]$TimeoutSec=30){
  $p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=$TimeoutSec}
  if($null -ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 80;$p.ContentType='application/json'}
  try{return Invoke-RestMethod @p}catch{ $status='';$text=''; if($_.Exception.Response){try{$status="; httpStatus=$([int]$_.Exception.Response.StatusCode)"}catch{};try{$r=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream());$text=$r.ReadToEnd();$r.Close()}catch{}}; if($text){$status="$status; response=$text"}; throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"}
}
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

$script:base=$BaseUrl.TrimEnd('/');$plainPassword=Convert-Secret $Password
$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sqlPath=Join-Path $scriptRoot 'lga-07-hotfix-07-4-inventory-adjustment-contract-alignment-check.sql'
$runtimeDir=Join-Path $repo '.runtime\lga-07-hotfix-07-4-inventory-adjustment-contract-alignment'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null

Write-Step "Validator version $script:ValidatorVersion"
Write-Step 'Repository/document HOTFIX LGA-07.4 guardrails...'
Assert-True (Test-Path (Join-Path $repo 'solidpos-platform.sln')) 'solidpos-platform.sln missing'
Assert-True (Test-Path $sqlPath) 'LGA-07.3 inventory diagnostic SQL missing'
Assert-DocumentContains (Join-Path $repo 'docs\ga\lga-07-hotfix-07-4-inventory-adjustment-contract-alignment-diagnostics-and-correction.md') @('lga-07.4','inventory adjustment contract alignment','negative stock regression','correction adjustment type','applyinventorycorrection','public ga not activated','no baseline increase')
Write-Step 'Repository/document HOTFIX LGA-07.4 guardrails PASS'

Write-Step 'Local build/test/secret guardrails...'
Invoke-Checked 'dotnet build' { dotnet build (Join-Path $repo 'solidpos-platform.sln') --nologo }
Invoke-Checked 'dotnet test' { dotnet test (Join-Path $repo 'solidpos-platform.sln') --no-build --nologo }
$secretScan=Join-Path $repo 'scripts\security\scan-local-secrets.ps1'
if(Test-Path $secretScan){Invoke-Checked 'secret scan' { & $secretScan }}
if($SkipDashboardBuild.IsPresent){Write-Step 'PosDashboard build skipped by switch.'}
Write-Step 'Local build/test/secret guardrails PASS'

Write-Step 'Limited GA API reachability checks...'
$login=Invoke-Api 'POST' '/api/v1/auth/login' @{tenantId=$TenantId;email=$Email;password=$plainPassword}
$accessToken=Req $login 'accessToken';$user=Req $login 'user';$createdByUserId=[string](Req $user 'id');$headers=@{Authorization="Bearer $accessToken"}
$live=Get-HttpStatus GET "$script:base/health/live"
$ready=Get-HttpStatus GET "$script:base/health/ready"
$inventory=Get-HttpStatus GET "$script:base/api/v1/inventory/stock" $headers
Assert-True ($live -eq 200) "/health/live must be 200; status=$live"
Assert-True ($ready -eq 200) "/health/ready must be 200; status=$ready"
Assert-True ($inventory -eq 200) "inventory stock endpoint must be 200; status=$inventory"
Write-Step 'Limited GA API reachability checks PASS'

Write-Step 'Negative stock regression diagnostics snapshot...'
$db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId} 'LGA074_INVENTORY_JSON:'
$negativeStock=Req $db 'negativeStock';$negativeCount=[int64](Req $negativeStock 'count')
$items=@(Req $negativeStock 'items')
Write-Step "Negative stock before correction: count=$negativeCount"
$correctionsApplied=@()
if($negativeCount -gt 0){
  foreach($item in $items){
    Write-Step ("Negative item: sku={0} store={1} quantityOnHand={2} adjustmentNeeded={3}" -f (Req $item 'sku'),(Req $item 'store_id'),(Req $item 'quantity_on_hand'),(Req $item 'adjustment_quantity_needed'))
  }
}

if($ApplyInventoryCorrection.IsPresent -and $negativeCount -gt 0){
  Write-Step 'Applying controlled inventory corrections via API...'
  foreach($item in $items){
    $body=@{
      localAdjustmentId=[guid]::NewGuid()
      storeId=[string](Req $item 'store_id')
      adjustmentType='correction'
      reason=$CorrectionReason
      createdByUserId=$createdByUserId
      occurredAt=(Get-Date).ToUniversalTime().ToString('o')
      lines=@(@{
        productId=[string](Req $item 'product_id')
        variantId=(Optional-Prop $item 'variant_id')
        quantityDelta=[string](Req $item 'adjustment_quantity_needed')
        unitId=[string](Req $item 'unit_id')
        costCents=$null
      })
    }
    $resp=Invoke-Api 'POST' '/api/v1/inventory/adjustments' $body $headers 45
    $correctionsApplied += [ordered]@{sku=(Req $item 'sku');storeId=[string](Req $item 'store_id');quantityDelta=[string](Req $item 'adjustment_quantity_needed');response=$resp}
  }
  $db=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId} 'LGA074_INVENTORY_JSON:'
  $negativeStock=Req $db 'negativeStock';$negativeCount=[int64](Req $negativeStock 'count')
  Write-Step "Negative stock after correction: count=$negativeCount"
}

$monitoring=Req $db 'monitoringActivity';$pressure=Req $db 'databasePressure';$sync=Req $db 'syncIntegrity'
$completed=[int64](Req $monitoring 'completed_sales_24h');$payments=[int64](Req $monitoring 'payments_24h');$receipts=[int64](Req $monitoring 'receipts_issued_24h')
$waiting=[int64](Req $pressure 'waiting_connection_count')
$classification=if($negativeCount -gt $AllowedNegativeStockItemCount){'NEGATIVE_STOCK_REMEDIATION_REQUIRED'}else{'NEGATIVE_STOCK_BASELINE_CLEAN'}
$recommendedAction=if($negativeCount -gt $AllowedNegativeStockItemCount){'Rerun this hotfix with -ApplyInventoryCorrection using adjustmentType=correction. Do not rerun LGA-07 until negativeStockCount is 0.'}else{'Rerun LGA-07 continued monitoring validator with AllowedNegativeStockItemCount 0 and AllowedWaitingConnectionCount 12.'}
if($ApplyInventoryCorrection.IsPresent -and $negativeCount -gt $AllowedNegativeStockItemCount){throw "Negative stock remains after correction; count=$negativeCount"}
Write-Step 'Negative stock regression diagnostics snapshot PASS'

$status=if($negativeCount -gt $AllowedNegativeStockItemCount){'PASS HOTFIX LGA-07.4 INVENTORY ADJUSTMENT CONTRACT ALIGNMENT DIAGNOSTICS / CORRECTION REQUIRED BEFORE LGA-07'}else{'PASS HOTFIX LGA-07.4 INVENTORY ADJUSTMENT CONTRACT ALIGNMENT CORRECTION / RERUN LGA-07'}
$manifest=[ordered]@{
  phase='LGA-07.4'
  status=$status
  validatorVersion=$script:ValidatorVersion
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  tenantId=$TenantId
  baseUrl=$BaseUrl
  dashboardUrl=$DashboardUrl
  allowedNegativeStockItemCount=$AllowedNegativeStockItemCount
  allowedWaitingConnectionCount=$AllowedWaitingConnectionCount
  applyInventoryCorrection=$ApplyInventoryCorrection.IsPresent
  negativeStockCount=$negativeCount
  negativeStock=$negativeStock
  completedSalesInLast24h=$completed
  paymentsInLast24h=$payments
  receiptsIssuedInLast24h=$receipts
  waitingConnectionCount=$waiting
  syncPendingCount=[int64](Req $sync 'sync_pending_count')
  syncProcessingCount=[int64](Req $sync 'sync_processing_count')
  syncRetryPendingCount=[int64](Req $sync 'sync_retry_pending_count')
  syncConflictCount=[int64](Req $sync 'sync_conflict_count')
  syncDeadLetterCount=[int64](Req $sync 'sync_dead_letter_count')
  correctionsApplied=@($correctionsApplied)
  classification=$classification
  recommendedAction=$recommendedAction
  databaseDiagnostics=$db
  publicGaActivation='NOT_ACTIVATED'
  nextPhase=if($negativeCount -gt $AllowedNegativeStockItemCount){'Apply LGA-07.4 correction, then rerun LGA-07'}else{'Rerun LGA-07 continued monitoring validator'}
}
$manifestPath=Join-Path $runtimeDir ("lga-07-hotfix-07-4-inventory-adjustment-contract-alignment-"+(Get-Date).ToString('yyyy-MM-dd-HHmmss')+'.json')
$manifest|ConvertTo-Json -Depth 100|Set-Content -Encoding UTF8 $manifestPath
Write-Step $status
$manifest|Format-List
