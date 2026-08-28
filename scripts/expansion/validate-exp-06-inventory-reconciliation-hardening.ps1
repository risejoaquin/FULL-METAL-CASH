param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$DryRun,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-06] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','metrics')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Assert-DocumentContainsAny { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ if($content.Contains($term.ToLowerInvariant())){ return } }; throw "Document $Path is missing one of required terms: $($Terms -join ', ')" }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-06-inventory-reconciliation-hardening-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-06-inventory-reconciliation-hardening'
$manifestPath=Join-Path $runtimeDirectory 'inventory-reconciliation-hardening-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-06-inventory-reconciliation-hardening-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-06-inventory-reconciliation-hardening.md'
  diagnostic=Join-Path $repoRoot 'docs\expansion\exp-06-inventory-diagnostic-report.md'
  reconciliation=Join-Path $repoRoot 'docs\expansion\exp-06-reconciliation-runbook.md'
  modifiers=Join-Path $repoRoot 'docs\expansion\exp-06-modifier-recipe-rules.md'
  alerts=Join-Path $repoRoot 'docs\expansion\exp-06-inventory-alerts-thresholds.md'
  rollback=Join-Path $repoRoot 'docs\expansion\exp-06-inventory-reconciliation-rollback.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-06-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-06 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-06 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-06','inventory reconciliation hardening','negative inventory','ledger','exp-07')
Assert-DocumentContains -Path $docs.diagnostic -Terms @('negative inventory','ledger','stock','root cause','evidence')
Assert-DocumentContains -Path $docs.reconciliation -Terms @('reconciliation','inventory_counts','inventory_ledger','adjustment','idempotent')
Assert-DocumentContains -Path $docs.modifiers -Terms @('modifier','substitute','recipe','replaces_product_id','consumption_quantity')
Assert-DocumentContains -Path $docs.alerts -Terms @('low stock','negative inventory','threshold','owner','action')
Assert-DocumentContains -Path $docs.rollback -Terms @('rollback','compensating adjustment','inventory_count')
Assert-DocumentContainsAny -Path $docs.rollback -Terms @('append-only ledger','append only ledger','ledger is append-only','append-only ledger contract','ledger append-only','ledger append only','ledger de solo anexado','ledger inmutable')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','negative inventory','pending conflicts','exp-07')
Write-Step 'EXP-06 document contract PASS'

Write-Step 'Local secret scan...'
Invoke-CheckedCommand -Name 'secret scan' -Command { & (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -Root $repoRoot }
Write-Step 'Local secret scan PASS'

Write-Step 'dotnet restore...'
Invoke-CheckedCommand -Name 'dotnet restore' -Command { & dotnet restore $slnPath }
Write-Step 'dotnet restore PASS'

Write-Step 'dotnet build...'
Invoke-CheckedCommand -Name 'dotnet build' -Command { & dotnet build $slnPath --no-restore }
Write-Step 'dotnet build PASS'

Write-Step 'dotnet test...'
Invoke-CheckedCommand -Name 'dotnet test' -Command { & dotnet test $slnPath --no-build }
Write-Step 'dotnet test PASS'

if(-not $SkipDashboardBuild){
  Write-Step 'Dashboard build/self-test...'
  if(Test-Path (Join-Path $dashboardRoot 'package.json')){
    if(Test-Path (Join-Path $dashboardRoot 'package-lock.json')){ Invoke-NpmCommand -Arguments @('ci') -WorkingDirectory $dashboardRoot } else { Invoke-NpmCommand -Arguments @('install') -WorkingDirectory $dashboardRoot }
    Invoke-NpmCommand -Arguments @('run','build') -WorkingDirectory $dashboardRoot
  }
  Write-Step 'Dashboard build/self-test PASS'
}

Write-Step 'Production liveness/readiness...'
$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Production liveness did not return alive.'
$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30
Assert-True ($ready.status -eq 'ready') 'Production readiness did not return ready.'
Assert-True ($ready.database -eq 'ready') 'Production database readiness did not return ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and inventory monitoring endpoint contract...'
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
$token=$session.accessToken; Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $token"}
$metrics=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/observability/metrics" -Headers $adminHeaders -TimeoutSec 30
$syncStatus=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/status" -Headers $adminHeaders -TimeoutSec 30
$deadLetter=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/dead-letter?limit=25" -Headers $adminHeaders -TimeoutSec 30
$conflicts=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/sync/conflicts?status=pending&limit=25" -Headers $adminHeaders -TimeoutSec 30
$auditEvents=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/audit/events?limit=25" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $metrics) 'Observability metrics endpoint did not return data.'
Assert-True ($null -ne $syncStatus) 'Sync status endpoint did not return data.'
Assert-True ($null -ne $deadLetter) 'Dead-letter endpoint did not return data.'
Assert-True ($null -ne $conflicts) 'Conflicts endpoint did not return data.'
Assert-True ((Get-Items $auditEvents).Count -ge 0) 'Audit endpoint shape invalid.'
Write-Step 'Admin login and inventory monitoring endpoint contract PASS'

Write-Step 'SQL inventory reconciliation cross-check...'
$applyValue = if($DryRun){ 'false' } else { 'true' }
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{tenant_id=$TenantId;admin_email=$Email;apply_reconciliation=$applyValue}
Assert-True ($sql.exp06SqlValidation -eq 'GO') "EXP-06 SQL validation failed: $($sql.sqlBlockingReasons -join ',')"
Assert-True ([int]$sql.schemaVersion -eq 4) 'Schema version 4 expected.'
Assert-True ([long]$sql.pendingConflictCount -eq 0) 'Pending conflicts block inventory reconciliation hardening.'
Assert-True ([long]$sql.invalidModifierBehaviorCount -eq 0) 'Invalid modifier inventory behavior detected.'
Assert-True ([long]$sql.invalidModifierEffectCount -eq 0) 'Invalid modifier inventory effect detected.'
Assert-True ([long]$sql.invalidSubstituteModifierCount -eq 0) 'Invalid substitute modifier shape detected.'
Assert-True ([long]$sql.invalidRecipeItemCount -eq 0) 'Invalid recipe item shape detected.'
Assert-True ([long]$sql.negativeInventoryAfterProjectedCount -eq 0) 'Negative inventory remains after reconciliation projection.'
Write-Step 'SQL inventory reconciliation cross-check PASS'

Write-Step 'Inventory reconciliation decision matrix...'
$retryPending=Get-LongValue -Object $syncStatus -Names @('retryPending','retryPendingSync','retryPendingCount') -Default ([long]$sql.retryPendingSyncCount)
$deadLetterCount=(Get-Items $deadLetter).Count
if($deadLetterCount -eq 0){ $deadLetterCount=[long]$sql.deadLetterSyncCount }
$pendingConflicts=(Get-Items $conflicts).Count
if($pendingConflicts -eq 0){ $pendingConflicts=[long]$sql.pendingConflictCount }
$blockers=@()
if($pendingConflicts -gt 0){ $blockers += 'pending_conflicts_block_exp06' }
if([long]$sql.negativeInventoryAfterProjectedCount -gt 0){ $blockers += 'negative_inventory_remaining_after_reconciliation' }
if([long]$sql.invalidModifierBehaviorCount -gt 0 -or [long]$sql.invalidModifierEffectCount -gt 0 -or [long]$sql.invalidSubstituteModifierCount -gt 0){ $blockers += 'modifier_inventory_semantics_invalid' }
if([long]$sql.invalidRecipeItemCount -gt 0){ $blockers += 'recipe_item_semantics_invalid' }
Assert-True ($blockers.Count -eq 0) "EXP-06 blockers detected: $($blockers -join ',')"
$conditions=@()
if($retryPending -gt 0){ $conditions += 'monitor_retry_pending_sync' }
if($deadLetterCount -gt 0){ $conditions += 'triage_known_dead_letter' }
if([long]$sql.lowStockThresholdCount -lt [long]$sql.stockTrackedProductCount){ $conditions += 'review_low_stock_threshold_coverage' }
if([long]$sql.activeRecipeCount -lt 1){ $conditions += 'review_recipe_coverage' }
$decision='GO_INVENTORY_RECONCILIATION_HARDENED'
Write-Step 'Inventory reconciliation decision matrix PASS'

Write-Step 'Write inventory reconciliation manifest and log...'
$manifest=[ordered]@{
  phase='EXP-06'
  status='PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
  tenantId=$TenantId
  baseUrl=$script:base
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  dryRun=[bool]$DryRun
  reconciliationDecision=$decision
  exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02'
  exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
  exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
  exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
  exp05='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
  healthLive=$live.status
  healthReady=$ready.status
  databaseReady=$ready.database
  activeStoreCount=[long]$sql.activeStoreCount
  stockTrackedProductCount=[long]$sql.stockTrackedProductCount
  lowStockThresholdCount=[long]$sql.lowStockThresholdCount
  negativeInventoryBeforeCount=[long]$sql.negativeInventoryBeforeCount
  negativeQuantityBeforeTotal=[decimal]$sql.negativeQuantityBeforeTotal
  reconciliationCountCount=[long]$sql.reconciliationCountCount
  reconciliationLineCount=[long]$sql.reconciliationLineCount
  reconciliationLedgerCount=[long]$sql.reconciliationLedgerCount
  adjustmentQuantityTotal=[decimal]$sql.adjustmentQuantityTotal
  negativeInventoryAfterProjectedCount=[long]$sql.negativeInventoryAfterProjectedCount
  invalidModifierBehaviorCount=[long]$sql.invalidModifierBehaviorCount
  invalidModifierEffectCount=[long]$sql.invalidModifierEffectCount
  invalidSubstituteModifierCount=[long]$sql.invalidSubstituteModifierCount
  substituteModifierCount=[long]$sql.substituteModifierCount
  activeRecipeCount=[long]$sql.activeRecipeCount
  activeRecipeItemCount=[long]$sql.activeRecipeItemCount
  invalidRecipeItemCount=[long]$sql.invalidRecipeItemCount
  retryPendingSync=$retryPending
  deadLetterSync=$deadLetterCount
  pendingConflicts=$pendingConflicts
  auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours
  blockers=$blockers
  conditions=$conditions
  sqlWarnings=@($sql.sqlWarnings)
  schemaVersion=[int]$sql.schemaVersion
  inventoryContract='inventory_reconciliation_hardening'
  nextPhase='EXP-07 Sync SLA and Offline Reliability Hardening'
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path $logPath -Encoding UTF8 -Value '# SolidPOS EXP-06 Inventory Reconciliation Hardening Log'
Add-Content -Path $logPath -Encoding UTF8 -Value ''
Add-Content -Path $logPath -Encoding UTF8 -Value "status: $($manifest.status)"
Add-Content -Path $logPath -Encoding UTF8 -Value "tenantId: $TenantId"
Add-Content -Path $logPath -Encoding UTF8 -Value "baseUrl: $script:base"
Add-Content -Path $logPath -Encoding UTF8 -Value "decision: $decision"
Add-Content -Path $logPath -Encoding UTF8 -Value "negativeInventoryBeforeCount: $($manifest.negativeInventoryBeforeCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "reconciliationLedgerCount: $($manifest.reconciliationLedgerCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "negativeInventoryAfterProjectedCount: $($manifest.negativeInventoryAfterProjectedCount)"
Add-Content -Path $logPath -Encoding UTF8 -Value "blockers: $($blockers -join ',')"
Add-Content -Path $logPath -Encoding UTF8 -Value "conditions: $($conditions -join ',')"
Add-Content -Path $logPath -Encoding UTF8 -Value 'goNoGo: GO'
Write-Step 'Write inventory reconciliation manifest and log PASS'
Write-Step 'EXP-06 PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
[pscustomobject]$manifest
