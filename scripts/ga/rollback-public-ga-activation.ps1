param(
  [Parameter(Mandatory=$true)][string]$TenantId,
  [Parameter(Mandatory=$true)][string]$DatabaseUrl,
  [Parameter(Mandatory=$true)][ValidateSet('ROLLBACK_PUBLIC_GA')][string]$ConfirmationPhrase,
  [Parameter(Mandatory=$true)][string]$Reason
)
$ErrorActionPreference='Stop'
$scriptRoot=Split-Path -Parent $PSCommandPath
$sql=Join-Path $scriptRoot 'public-ga-activation-rollback.sql'
$check=Join-Path $scriptRoot 'public-ga-activation-state-check.sql'
function Invoke-Db([string]$file,[hashtable]$vars){
  $cmd=Get-Command psql -ErrorAction SilentlyContinue
  if(-not $cmd){throw 'psql is required for explicit rollback.'}
  $args=@($DatabaseUrl,'-X','-q','-tA','-P','footer=off','-v','ON_ERROR_STOP=1')
  foreach($k in $vars.Keys){$args += @('-v',"$k=$($vars[$k])")};$args+=@('-f',(Resolve-Path $file).Path)
  & $cmd.Source @args
  if($LASTEXITCODE -ne 0){throw "Database SQL failed: $file"}
}
Invoke-Db $sql @{tenant_id=$TenantId;confirmation_phrase=$ConfirmationPhrase;rollback_reason=$Reason}
Invoke-Db $check @{tenant_id=$TenantId}
Write-Host '[PUBLIC-GA-ROLLBACK] PASS / PUBLIC GA RETURNED TO NOT_ACTIVATED'
