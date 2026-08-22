param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[BETA-09] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; $global:LASTEXITCODE=0; return ($json|ConvertFrom-Json) }
$base=$BaseUrl.TrimEnd('/')
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$beta08=Join-Path $repoRoot 'scripts\beta\validate-beta-08-customer-acceptance-validation.ps1'
$beta08Manifest=Join-Path $repoRoot '.runtime\beta-08-customer-acceptance-validation\beta-08-customer-acceptance-manifest.json'
$exp06=Join-Path $repoRoot 'scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1'
$exp06Manifest=Join-Path $repoRoot '.runtime\exp-06-inventory-reconciliation-hardening\inventory-reconciliation-hardening-manifest.json'
$sqlPath=Join-Path $scriptRoot 'beta-09-data-quality-reconciliation-closure-check.sql'
$shiftTriageSql=Join-Path $scriptRoot 'beta-09-stale-validation-shift-triage.sql'
$shiftCleanupSql=Join-Path $scriptRoot 'beta-09-close-stale-validation-shifts.sql'
$runtime=Join-Path $repoRoot '.runtime\beta-09-data-quality-reconciliation-closure'
$manifestPath=Join-Path $runtime 'beta-09-data-quality-reconciliation-manifest.json'
$reportPath=Join-Path $runtime 'beta-09-reconciliation-report.md'
$logPath=Join-Path $repoRoot 'docs\beta\logs\beta-09-data-quality-reconciliation-closure-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_BETA_09_BETA_DATA_QUALITY_AND_RECONCILIATION_CLOSURE.md'),
 (Join-Path $repoRoot 'docs\beta\beta-09-data-quality-reconciliation-closure.md'),
 (Join-Path $repoRoot 'docs\beta\beta-09-reconciliation-matrix.md'),
 (Join-Path $repoRoot 'docs\beta\beta-09-data-quality-rules.md'),
 (Join-Path $repoRoot 'docs\beta\beta-09-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
Write-Step 'Repository/document guardrails...'
Assert-True (Test-Path $beta08) 'BETA-08 validator missing.'
Assert-True (Test-Path $exp06) 'EXP-06 inventory reconciliation validator missing.'
Assert-True (Test-Path $sqlPath) 'BETA-09 SQL validator missing.'
Assert-True (Test-Path $shiftTriageSql) 'BETA-09 stale shift triage SQL missing.'
Assert-True (Test-Path $shiftCleanupSql) 'BETA-09 stale shift cleanup SQL missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required BETA-09 document missing: $d"}
Assert-DocumentContains $docs[0] @('BETA-09','data quality','reconciliation','sales','cash','inventory','catalog/pricing','customer/user','sync','audit','release','GO BETA-10')
Assert-DocumentContains $docs[2] @('sales reconciliation','cash reconciliation','inventory reconciliation','catalog/pricing consistency','customer/user consistency','sync consistency','audit consistency','release consistency')
Assert-DocumentContains $docs[3] @('no negative price','no invalid price window','no invalid tax mode','no invalid modifier behavior','no untriaged new dead-letter','no unresolved conflicts','cash differences reviewed','open shifts reviewed')
Assert-DocumentContains $docs[4] @('blockers = {}','schemaVersion = 4','syncContract = schema_version_4','GO BETA-10')
Write-Step 'Repository/document guardrails PASS'
Write-Step 'Execute BETA-08 customer acceptance prerequisite...'
Unblock-File $beta08 -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\beta\validate-beta-07-dashboard-daily-monitoring-pack.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -ErrorAction SilentlyContinue
& $beta08 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
if($LASTEXITCODE -ne 0){throw "BETA-08 inherited validator failed with exit code $LASTEXITCODE."}
Assert-True (Test-Path $beta08Manifest) 'BETA-08 manifest missing.'
$accept=Get-Content -Raw $beta08Manifest | ConvertFrom-Json
Assert-True ($accept.status -eq 'PASS BETA CUSTOMER ACCEPTANCE VALIDATION / GO BETA-09') 'BETA-08 prerequisite did not PASS.'
$baselineAt=[string]$accept.generatedAt
Write-Step 'Execute BETA-08 customer acceptance prerequisite PASS'
Write-Step 'Execute inventory reconciliation hardening for closure...'
Unblock-File $exp06 -ErrorAction SilentlyContinue
& $exp06 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
if($LASTEXITCODE -ne 0){throw "EXP-06 inventory reconciliation failed with exit code $LASTEXITCODE."}
Assert-True (Test-Path $exp06Manifest) 'EXP-06 reconciliation manifest missing.'
$inventory=Get-Content -Raw $exp06Manifest | ConvertFrom-Json
Assert-True ($inventory.status -eq 'PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07') 'Inventory reconciliation prerequisite did not PASS.'
Assert-True ([long]$inventory.negativeInventoryAfterProjectedCount -eq 0) 'Inventory reconciliation did not project zero negative inventory.'
Write-Step 'Execute inventory reconciliation hardening for closure PASS'
Write-Step 'Triage stale open cash shifts before closure...'
$shiftTriage=Invoke-DbJsonFile $shiftTriageSql @{tenant_id=$TenantId}
Assert-True ([long]$shiftTriage.nonValidationStaleShiftCount -eq 0) "Stale open cash shift belongs to a non-validation terminal; operator review required. Shift evidence: $($shiftTriage.staleShifts | ConvertTo-Json -Compress -Depth 5)"
$closedValidationShiftCount=0
$closedValidationShiftAuditCount=0
if([long]$shiftTriage.validationOwnedStaleShiftCount -gt 0){
  Write-Step "Closing $($shiftTriage.validationOwnedStaleShiftCount) stale validation-owned cash shift fixture(s) with zero difference and audit evidence..."
  $cleanup=Invoke-DbJsonFile $shiftCleanupSql @{tenant_id=$TenantId}
  $closedValidationShiftCount=[long]$cleanup.closedValidationShiftCount
  $closedValidationShiftAuditCount=[long]$cleanup.auditEventCount
  Assert-True ($closedValidationShiftCount -eq [long]$shiftTriage.validationOwnedStaleShiftCount) 'Not all validation-owned stale shifts were closed.'
  Assert-True ($closedValidationShiftAuditCount -eq $closedValidationShiftCount) 'Stale validation shift cleanup audit evidence count mismatch.'
}
Write-Step 'Triage stale open cash shifts before closure PASS'
Write-Step 'SQL cross-domain reconciliation closure...'
$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;baseline_at=$baselineAt}
Assert-True ($sql.beta09SqlDecision -eq 'GO') "BETA-09 SQL blockers: $($sql.blockers -join ', ')"
Write-Step 'SQL cross-domain reconciliation closure PASS'
Write-Step 'Build reconciliation report and evidence manifest...'
$conditions=@($sql.conditions)
$blockers=@($sql.blockers)
$generated=(Get-Date).ToUniversalTime().ToString('o')
@"
# BETA-09 Data Quality and Reconciliation Closure Report

Generated: $generated
Tenant: $TenantId
Baseline: $baselineAt

## Reconciliation domains
- sales reconciliation: PASS — payment mismatch count = $($sql.salePaymentMismatchCount)
- cash reconciliation: PASS — cash differences last 24h = $($sql.cashDifferenceLast24HoursCount); stale open shifts = $($sql.staleOpenShiftCount)
- inventory reconciliation: PASS — negative inventory = $($sql.negativeInventoryItemCount)
- catalog/pricing consistency: PASS — negative price = $($sql.negativePriceCount); invalid windows = $($sql.invalidPriceWindowCount); invalid tax modes = $($sql.invalidTaxModeCount); invalid modifier behavior = $($sql.invalidModifierBehaviorCount)
- customer/user consistency: PASS — orphan roles = $($sql.orphanUserRoleCount); orphan store access = $($sql.orphanStoreAccessCount)
- sync consistency: PASS — unresolved conflicts = $($sql.unresolvedConflictCount); new dead-letter = $($sql.newDeadLetterCount); legacy schema = $($sql.legacySchemaEventCount)
- audit consistency: PASS — audit events last 24h = $($sql.auditEventsLast24Hours)
- release consistency: PASS — active beta releases = $($sql.activeBetaReleaseCount); invalid active beta releases = $($sql.invalidBetaReleaseCount)

## Conditions
$(if($conditions.Count -eq 0){'- none'}else{($conditions|ForEach-Object{"- $_"}) -join "`n"})

## Closure
- blockers = {}
- schemaVersion = 4
- syncContract = schema_version_4
- decision = GO BETA-10
"@ | Set-Content -Encoding UTF8 $reportPath
$manifest=[ordered]@{
 phase='BETA-09';status='PASS BETA DATA QUALITY RECONCILIATION CLOSURE / GO BETA-10';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;betaDecision='GO_BETA_10';dataQualityContract='beta_data_quality_reconciliation_closure';baselineAt=$baselineAt;
 salesReconciliation='PASS';cashReconciliation='PASS';inventoryReconciliation='PASS';catalogPricingConsistency='PASS';customerUserConsistency='PASS';syncConsistency='PASS';auditConsistency='PASS';releaseConsistency='PASS';
 reconciledSaleCount=[long]$sql.reconciledSaleCount;salePaymentMismatchCount=[long]$sql.salePaymentMismatchCount;orphanActiveReceiptCount=[long]$sql.orphanActiveReceiptCount;returnRefundMismatchCount=[long]$sql.returnRefundMismatchCount;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;openShiftCount=[long]$sql.openShiftCount;staleOpenShiftCount=[long]$sql.staleOpenShiftCount;closedValidationStaleShiftCount=$closedValidationShiftCount;closedValidationStaleShiftAuditCount=$closedValidationShiftAuditCount;negativeInventoryItemCount=[long]$sql.negativeInventoryItemCount;negativePriceCount=[long]$sql.negativePriceCount;invalidPriceWindowCount=[long]$sql.invalidPriceWindowCount;invalidTaxModeCount=[long]$sql.invalidTaxModeCount;invalidModifierBehaviorCount=[long]$sql.invalidModifierBehaviorCount;invalidSubstituteModifierCount=[long]$sql.invalidSubstituteModifierCount;orphanUserRoleCount=[long]$sql.orphanUserRoleCount;orphanStoreAccessCount=[long]$sql.orphanStoreAccessCount;processedSchema4SyncCount=[long]$sql.processedSchema4SyncCount;legacySchemaEventCount=[long]$sql.legacySchemaEventCount;unresolvedConflictCount=[long]$sql.unresolvedConflictCount;deadLetterCount=[long]$sql.deadLetterCount;newDeadLetterCount=[long]$sql.newDeadLetterCount;untriagedDeadLetterCount=[long]$sql.untriagedDeadLetterCount;auditEventsLast24Hours=[long]$sql.auditEventsLast24Hours;activeBetaReleaseCount=[long]$sql.activeBetaReleaseCount;invalidBetaReleaseCount=[long]$sql.invalidBetaReleaseCount;conditions=$conditions;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';nextPhase='BETA-10 - Limited Commercial Beta Closure Report'
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-09 Data Quality and Reconciliation Closure Log

- status: $($manifest.status)
- generatedAt: $generated
- baselineAt: $baselineAt
- negativeInventoryItemCount: $($manifest.negativeInventoryItemCount)
- newDeadLetterCount: $($manifest.newDeadLetterCount)
- unresolvedConflictCount: $($manifest.unresolvedConflictCount)
- blockers: $($blockers -join ', ')
- conditions: $($conditions -join ', ')
- schemaVersion: 4
- syncContract: schema_version_4
"@ | Set-Content -Encoding UTF8 $logPath
Assert-True ($blockers.Count -eq 0) "BETA-09 blockers: $($blockers -join ', ')"
Write-Step 'BETA-09 evidence manifest PASS'
Write-Step 'BETA-09 PASS BETA DATA QUALITY RECONCILIATION CLOSURE / GO BETA-10'
[pscustomobject]$manifest | Format-List
