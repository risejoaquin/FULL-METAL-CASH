param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [string]$StoreCode='MAIN',
    [string]$ProductSku='QSR-AMERICANO',
    [string]$DatabasePath='.\.runtime\beta-04-offline-reliability-field-run.sqlite',
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step([string]$m){Write-Host "[BETA-04] $m"}
function Assert-True([bool]$c,[string]$m){if(-not $c){throw $m}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;&$c;if($LASTEXITCODE-ne0){throw "$n failed with exit code $LASTEXITCODE"};$global:LASTEXITCODE=0}
function Invoke-DbScalar([string]$Sql){$global:LASTEXITCODE=0;$o=docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:17 psql "$DatabaseUrl" -tAc $Sql;if($LASTEXITCODE-ne0){throw 'DB scalar command failed.'};$global:LASTEXITCODE=0;return ($o|Select-Object -First 1).Trim()}
function Invoke-DbJson([string]$SqlPath,[hashtable]$Vars){$d=(Resolve-Path(Split-Path -Parent $SqlPath)).Path;$f=Split-Path -Leaf $SqlPath;$a=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${d}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$a+=@('-v',"$k=$($Vars[$k])")};$a+=@('-f',"/sql/$f");$global:LASTEXITCODE=0;$o=docker @a;if($LASTEXITCODE-ne0){throw 'DB SQL validator failed.'};$global:LASTEXITCODE=0;return (($o|Where-Object{$_}|Select-Object -Last 1)|ConvertFrom-Json)}

$script:base=$BaseUrl.TrimEnd('/')
$scriptRoot=Split-Path -Parent $PSCommandPath
$repo=Resolve-Path(Join-Path $scriptRoot '..\..')
$sln=Join-Path $repo 'solidpos-platform.sln'
$pilot=Join-Path $repo 'scripts\pilot\validate-offline-mode-field-test.ps1'
$sqlPath=Join-Path $scriptRoot 'beta-04-offline-reliability-field-run-check.sql'
$runtime=Join-Path $repo '.runtime\beta-04-offline-reliability-field-run'
$manifestPath=Join-Path $runtime 'beta-04-offline-reliability-manifest.json'
$logPath=Join-Path $repo 'docs\beta\logs\beta-04-offline-reliability-field-run-log.md'
New-Item -ItemType Directory -Force $runtime,(Split-Path $logPath)|Out-Null

Write-Step 'Repository guardrails...'
Assert-True (Test-Path $sln) 'solution missing'
Assert-True (Test-Path $pilot) 'PILOT-05 offline validator missing'
Assert-True (Test-Path $sqlPath) 'BETA-04 SQL validator missing'
Assert-True ($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.'
# Prevent the nested validated script from triggering the downloaded-script prompt.
Unblock-File $pilot -ErrorAction SilentlyContinue
Write-Step 'Repository guardrails PASS'

Write-Step 'Local secret scan...';Invoke-Checked 'secret scan' {& (Join-Path $repo 'scripts\security\scan-local-secrets.ps1') -Root $repo};Write-Step 'Local secret scan PASS'
Write-Step 'dotnet restore...';Invoke-Checked 'dotnet restore' {dotnet restore $sln};Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...';Invoke-Checked 'dotnet build' {dotnet build $sln --no-restore};Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...';Invoke-Checked 'dotnet test' {dotnet test $sln --no-build};Write-Step 'dotnet test PASS'

Write-Step 'Capture pre-run sync reliability baseline...'
$baselineDeadLetter=[long](Invoke-DbScalar "select count(*) from pos.sync_inbox_events where tenant_id = '$TenantId'::uuid and status = 'dead_letter';")
$baselinePendingConflict=[long](Invoke-DbScalar "select count(*) from pos.sync_conflicts where tenant_id = '$TenantId'::uuid and status = 'pending';")
Write-Step 'Capture pre-run sync reliability baseline PASS'

Write-Step 'Executing controlled offline-to-online field run...'
$pilotArgs=@{BaseUrl=$script:base;TenantId=$TenantId;Email=$Email;Password=$Password;DatabaseUrl=$DatabaseUrl;StoreCode=$StoreCode;ProductSku=$ProductSku;DatabasePath=$DatabasePath}
if($SkipDashboardBuild){$pilotArgs.SkipDashboardValidation=$true}
$pilotOutput=& $pilot @pilotArgs
$op=$pilotOutput | Where-Object { $null -ne $_.goNoGo -and $null -ne $_.localSaleId } | Select-Object -Last 1
Assert-True ($null -ne $op) 'Underlying PILOT-05 flow did not return its evidence object.'
Assert-True ($op.goNoGo -eq 'GO') 'Underlying offline field run did not return GO.'
Assert-True ([int]$op.schemaVersion -eq 4) 'Offline field run schemaVersion must be 4.'
Assert-True ($op.syncContract -eq 'schema_version_4') 'Offline field run sync contract mismatch.'
Assert-True ([int]$op.syncStatusDeadLetterCount -eq 0) 'Controlled terminal produced dead-letter events.'
Assert-True ([int]$op.deadLetterListCount -eq 0) 'Controlled terminal dead-letter list must be empty.'
Write-Step 'Executing controlled offline-to-online field run PASS'

Write-Step 'SQL duplicate/dead-letter/conflict reconciliation...'
$sql=Invoke-DbJson $sqlPath @{tenant_id=$TenantId;store_id=$op.storeId;terminal_id=$op.terminalId;local_sale_id=$op.localSaleId;sale_id=$op.remoteSaleId;batch_id=$op.batchId;baseline_dead_letter_count=$baselineDeadLetter;baseline_pending_conflict_count=$baselinePendingConflict}
Assert-True (@($sql.blockers).Count -eq 0) "BETA-04 SQL blockers: $(@($sql.blockers) -join ', ')"
Assert-True ($sql.decision -eq 'GO') 'BETA-04 SQL decision was not GO.'
Write-Step 'SQL duplicate/dead-letter/conflict reconciliation PASS'

$conditions=@()
if($baselineDeadLetter -gt 0){$conditions+='preexisting_dead_letter_requires_triage'}
$manifest=[ordered]@{
  phase='BETA-04';status='PASS BETA OFFLINE RELIABILITY FIELD RUN / GO BETA-05';tenantId=$TenantId;baseUrl=$script:base;generatedAt=(Get-Date).ToUniversalTime().ToString('o');betaDecision='GO_BETA_05';
  storeId=$op.storeId;terminalId=$op.terminalId;localDatabasePath=$op.localDatabasePath;localSaleId=$op.localSaleId;remoteSaleId=$op.remoteSaleId;receiptId=$op.receiptId;batchId=$op.batchId;duplicateBatchId=$op.duplicateBatchId;
  processedCount=[int]$op.processedCount;saleCount=[int]$sql.saleCount;approvedCashPaymentCount=[int]$sql.approvedCashPaymentCount;processedSaleEventCount=[int]$sql.processedSaleEventCount;
  baselineDeadLetterCount=[int]$sql.baselineDeadLetterCount;finalDeadLetterCount=[int]$sql.finalDeadLetterCount;newDeadLetterCount=[int]$sql.newDeadLetterCount;
  baselinePendingConflictCount=[int]$sql.baselinePendingConflictCount;finalPendingConflictCount=[int]$sql.finalPendingConflictCount;newPendingConflictCount=[int]$sql.newPendingConflictCount;
  legacySchemaEventCount=[int]$sql.legacySchemaEventCount;idempotencyDuplicateCheck='PASS';pullSyncReadModels='PASS';offlineLogin='PASS';outboxGeneration='PASS';
  blockers=@();conditions=$conditions;schemaVersion=4;syncContract='schema_version_4';nextPhase='BETA-05 - Beta Support Operations Drill'
}
$manifest|ConvertTo-Json -Depth 20|Set-Content -Encoding UTF8 $manifestPath
Set-Content -Path $logPath -Encoding UTF8 -Value '# BETA-04 Offline Reliability Field Run Log'
Add-Content -Path $logPath -Encoding UTF8 -Value ''
Add-Content -Path $logPath -Encoding UTF8 -Value "- status: $($manifest.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- generatedAt: $($manifest.generatedAt)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- terminalId: $($manifest.terminalId)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- localSaleId: $($manifest.localSaleId)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- remoteSaleId: $($manifest.remoteSaleId)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- saleCount: $($manifest.saleCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- approvedCashPaymentCount: $($manifest.approvedCashPaymentCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- newDeadLetterCount: $($manifest.newDeadLetterCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "- newPendingConflictCount: $($manifest.newPendingConflictCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value '- blockers: {}'
Add-Content -Path $logPath -Encoding UTF8 -Value '- schemaVersion: 4'
Add-Content -Path $logPath -Encoding UTF8 -Value '- syncContract: schema_version_4'
Write-Step 'BETA-04 evidence manifest PASS'
Write-Step 'BETA-04 PASS BETA OFFLINE RELIABILITY FIELD RUN / GO BETA-05'
$manifest
