param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[GA-04] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try {[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)} finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)} }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$ga03=Join-Path $scriptRoot 'validate-ga-03-support-incident-slo-operations-readiness.ps1'
$ga03Manifest=Join-Path $repoRoot '.runtime\ga-03-support-incident-slo-operations-readiness\ga-03-manifest.json'
$secretScan=Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1'
$checkSql=Join-Path $scriptRoot 'ga-04-production-data-integrity-financial-reconciliation-check.sql'
$runtime=Join-Path $repoRoot '.runtime\ga-04-production-data-integrity-financial-reconciliation'
$manifestPath=Join-Path $runtime 'ga-04-manifest.json'
$evidencePath=Join-Path $runtime 'ga-04-evidence.md'
$snapshotPath=Join-Path $runtime 'ga-04-snapshot.json'
$logPath=Join-Path $repoRoot 'docs\ga\logs\ga-04-production-data-integrity-financial-reconciliation-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
 (Join-Path $repoRoot 'SOLIDPOS_GA_04_PRODUCTION_DATA_INTEGRITY_AND_FINANCIAL_RECONCILIATION_GATE.md'),
 (Join-Path $repoRoot 'docs\ga\ga-04-production-data-integrity-financial-reconciliation.md'),
 (Join-Path $repoRoot 'docs\ga\ga-04-reconciliation-evidence-matrix.md'),
 (Join-Path $repoRoot 'docs\ga\ga-04-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document GA-04 guardrails...'
Assert-True (Test-Path $ga03) 'GA-03 validator missing.'
Assert-True (Test-Path $secretScan) 'Secret scan missing.'
Assert-True (Test-Path $checkSql) 'GA-04 SQL check missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required GA-04 document missing: $d"}
Assert-DocumentContains $docs[0] @('GA-04','Production Data Integrity and Financial Reconciliation Gate','Sales / Payments','Returns / Refunds','Inventory','PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05')
$ga04MainState=(Get-Content -Raw $docs[1]).ToLowerInvariant(); Assert-True ($ga04MainState.Contains('pending user validation') -or $ga04MainState.Contains('pass real production')) 'GA-04 main document must contain a valid lifecycle state.'
Assert-DocumentContains $docs[1] @('schemaVersion = 4','syncContract = schema_version_4','generalAvailabilityActivated = False','all material mismatch counts must be zero')
Assert-DocumentContains $docs[2] @('sale/payment reconciliation','public token integrity','counted vs expected','ledger consistency','substitute semantics','orphan roles','invalid store access')
Assert-DocumentContains $docs[3] @('sales/payments','receipts','returns/refunds','cash','inventory','catalog/pricing','users/access','sync/audit')
$ga04GoState=(Get-Content -Raw $docs[4]).ToLowerInvariant(); Assert-True ($ga04GoState.Contains('pending user validation') -or $ga04GoState.Contains('pass real production')) 'GA-04 go/no-go document must contain a valid lifecycle state.'
Assert-DocumentContains $docs[4] @('PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05','FAIL / HOTFIX REQUIRED')
Write-Step 'Repository/document GA-04 guardrails PASS'

Write-Step 'Secret scan...'
Unblock-File $secretScan -ErrorAction SilentlyContinue
& $secretScan -Root $repoRoot
Write-Step 'Secret scan PASS'

Write-Step 'Fresh GA-03 prerequisite revalidation...'
Unblock-File $ga03 -ErrorAction SilentlyContinue
& $ga03 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
Assert-True (Test-Path $ga03Manifest) 'Fresh GA-03 manifest missing.'
$g3=Get-Content -Raw $ga03Manifest | ConvertFrom-Json
Assert-True ($g3.status -eq 'PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04') 'Fresh GA-03 prerequisite did not PASS.'
Assert-True (@($g3.blockers).Count -eq 0) 'Fresh GA-03 prerequisite contains blockers.'
Assert-True ([int]$g3.schemaVersion -eq 4) 'Fresh GA-03 schemaVersion drifted from 4.'
Assert-True ([string]$g3.syncContract -eq 'schema_version_4') 'Fresh GA-03 syncContract drifted.'
Assert-True (-not [bool]$g3.generalAvailabilityActivated) 'General Availability is already activated; GA-04 must stop.'
$ga03At=[string]$g3.generatedAt
Write-Step 'Fresh GA-03 prerequisite revalidation PASS'

Write-Step 'Production health and authentication gate...'
$live=Invoke-RestMethod -Method Get -Uri "$base/health/live" -TimeoutSec 30
$ready=Invoke-RestMethod -Method Get -Uri "$base/health/ready" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Production liveness is not alive.'
Assert-True ($ready.status -eq 'ready') 'Production readiness is not ready.'
Assert-True ($ready.database -eq 'ready') 'Production database readiness is not ready.'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Login did not return accessToken.'
Write-Step 'Production health and authentication gate PASS'

Write-Step 'GA-04 production reconciliation SQL...'
$sql=Invoke-DbJsonFile $checkSql @{tenant_id=$TenantId;ga03_at=$ga03At}
Assert-True ([string]$sql.ga04SqlContract -eq 'ga_production_data_integrity_financial_reconciliation') 'GA-04 SQL contract mismatch.'
Assert-True ([int]$sql.schemaVersion -eq 4) 'GA-04 SQL schemaVersion drifted.'
Assert-True ([string]$sql.syncContract -eq 'schema_version_4') 'GA-04 SQL syncContract drifted.'
Assert-True (-not [bool]$sql.generalAvailabilityActivated) 'GA-04 SQL indicates General Availability activated.'
Write-Step ("GA-04 reconciliation counts: sales={0}; saleTotalsMismatch={1}; salePaymentMismatch={2}; returnsMismatch={3}; cashFormulaMismatch={4}; negativeInventory={5}; invalidModifierSemantics={6}; blockers={7}" -f $sql.reconciledSaleCount,$sql.saleTotalsMismatchCount,$sql.salePaymentMismatchCount,$sql.returnRefundMismatchCount,$sql.cashFormulaMismatchCount,$sql.negativeInventoryItemCount,$sql.invalidModifierSemanticsCount,@($sql.blockers).Count)
$blockers=@($sql.blockers)
Assert-True ($blockers.Count -eq 0) "GA-04 blockers: $($blockers -join ', ')"
Assert-True ([string]$sql.ga04SqlDecision -eq 'GO') 'GA-04 SQL decision is not GO.'
Write-Step 'GA-04 production reconciliation SQL PASS'

$generated=(Get-Date).ToUniversalTime().ToString('o')
$snapshot=[ordered]@{phase='GA-04';generatedAt=$generated;tenantId=$TenantId;baseUrl=$base;ga03At=$ga03At;health=[ordered]@{live=$live.status;ready=$ready.status;database=$ready.database};sql=$sql;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false}
$snapshot | ConvertTo-Json -Depth 14 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-04 Production Data Integrity and Financial Reconciliation Evidence

Generated: $generated
Tenant: $TenantId
Fresh GA-03 generatedAt: $ga03At

## Material mismatch counts
- saleTotalsMismatchCount: $($sql.saleTotalsMismatchCount)
- salePaymentMismatchCount: $($sql.salePaymentMismatchCount)
- saleTenderMismatchCount: $($sql.saleTenderMismatchCount)
- orphanApprovedPaymentCount: $($sql.orphanApprovedPaymentCount)
- orphanActiveReceiptCount: $($sql.orphanActiveReceiptCount)
- invalidActiveReceiptTokenCount: $($sql.invalidActiveReceiptTokenCount)
- returnTotalsMismatchCount: $($sql.returnTotalsMismatchCount)
- returnRefundMismatchCount: $($sql.returnRefundMismatchCount)
- invalidReturnLineSaleReferenceCount: $($sql.invalidReturnLineSaleReferenceCount)
- cashFormulaMismatchCount: $($sql.cashFormulaMismatchCount)
- negativeInventoryItemCount: $($sql.negativeInventoryItemCount)
- invalidInventoryReferenceCount: $($sql.invalidInventoryReferenceCount)
- invalidModifierSemanticsCount: $($sql.invalidModifierSemanticsCount)
- orphanUserRoleCount: $($sql.orphanUserRoleCount)
- orphanStoreAccessCount: $($sql.orphanStoreAccessCount)
- retryPendingCount: $($sql.retryPendingCount)
- pendingConflictCount: $($sql.pendingConflictCount)
- legacySchemaEventCount: $($sql.legacySchemaEventCount)
- newDeadLetterSinceGa03Count: $($sql.newDeadLetterSinceGa03Count)

## Decision
PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05
General Availability remains NOT activated.
"@ | Set-Content -Encoding UTF8 $evidencePath
$manifest=[ordered]@{
 phase='GA-04';status='PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;entryGate='PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04';ga03At=$ga03At;reconciliationContract='ga_production_data_integrity_financial_reconciliation';
 reconciledSaleCount=[long]$sql.reconciledSaleCount;saleTotalsMismatchCount=[long]$sql.saleTotalsMismatchCount;salePaymentMismatchCount=[long]$sql.salePaymentMismatchCount;saleTenderMismatchCount=[long]$sql.saleTenderMismatchCount;orphanApprovedPaymentCount=[long]$sql.orphanApprovedPaymentCount;paymentCurrencyMismatchCount=[long]$sql.paymentCurrencyMismatchCount;returnedSaleWithoutCompletedReturnCount=[long]$sql.returnedSaleWithoutCompletedReturnCount;
 orphanActiveReceiptCount=[long]$sql.orphanActiveReceiptCount;invalidActiveReceiptTokenCount=[long]$sql.invalidActiveReceiptTokenCount;duplicateActiveReceiptTokenCount=[long]$sql.duplicateActiveReceiptTokenCount;duplicateReceiptNumberCount=[long]$sql.duplicateReceiptNumberCount;
 orphanCompletedReturnCount=[long]$sql.orphanCompletedReturnCount;returnTotalsMismatchCount=[long]$sql.returnTotalsMismatchCount;returnRefundMismatchCount=[long]$sql.returnRefundMismatchCount;invalidReturnLineSaleReferenceCount=[long]$sql.invalidReturnLineSaleReferenceCount;orphanApprovedRefundCount=[long]$sql.orphanApprovedRefundCount;
 openShiftCount=[long]$sql.openShiftCount;staleOpenShiftCount=[long]$sql.staleOpenShiftCount;cashFormulaMismatchCount=[long]$sql.cashFormulaMismatchCount;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;
 negativeInventoryItemCount=[long]$sql.negativeInventoryItemCount;invalidInventoryReferenceCount=[long]$sql.invalidInventoryReferenceCount;orphanSaleInventoryMovementCount=[long]$sql.orphanSaleInventoryMovementCount;orphanReturnInventoryMovementCount=[long]$sql.orphanReturnInventoryMovementCount;invalidActiveRecipeCount=[long]$sql.invalidActiveRecipeCount;invalidActiveRecipeItemCount=[long]$sql.invalidActiveRecipeItemCount;
 invalidProductPriceCount=[long]$sql.invalidProductPriceCount;invalidPriceWindowCount=[long]$sql.invalidPriceWindowCount;invalidTaxModeCount=[long]$sql.invalidTaxModeCount;invalidModifierBehaviorCount=[long]$sql.invalidModifierBehaviorCount;invalidModifierSemanticsCount=[long]$sql.invalidModifierSemanticsCount;
 orphanUserRoleCount=[long]$sql.orphanUserRoleCount;orphanStoreAccessCount=[long]$sql.orphanStoreAccessCount;inactiveAccessRelationshipCount=[long]$sql.inactiveAccessRelationshipCount;retryPendingCount=[long]$sql.retryPendingCount;pendingConflictCount=[long]$sql.pendingConflictCount;legacySchemaEventCount=[long]$sql.legacySchemaEventCount;newDeadLetterSinceGa03Count=[long]$sql.newDeadLetterSinceGa03Count;historicalDeadLetterDecisionAuditCount=[long]$sql.historicalDeadLetterDecisionAuditCount;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-05 - Stable Release Candidate Build, Signing and Provenance'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $manifestPath
@"
# GA-04 Production Data Integrity and Financial Reconciliation Log
- status: $($manifest.status)
- generatedAt: $generated
- reconciledSaleCount: $($manifest.reconciledSaleCount)
- saleTotalsMismatchCount: $($manifest.saleTotalsMismatchCount)
- salePaymentMismatchCount: $($manifest.salePaymentMismatchCount)
- returnRefundMismatchCount: $($manifest.returnRefundMismatchCount)
- cashFormulaMismatchCount: $($manifest.cashFormulaMismatchCount)
- negativeInventoryItemCount: $($manifest.negativeInventoryItemCount)
- invalidModifierSemanticsCount: $($manifest.invalidModifierSemanticsCount)
- orphanUserRoleCount: $($manifest.orphanUserRoleCount)
- orphanStoreAccessCount: $($manifest.orphanStoreAccessCount)
- retryPendingCount: $($manifest.retryPendingCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
- newDeadLetterSinceGa03Count: $($manifest.newDeadLetterSinceGa03Count)
- blockers: none
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: false
- nextPhase: GA-05 - Stable Release Candidate Build, Signing and Provenance
"@ | Set-Content -Encoding UTF8 $logPath
Write-Step 'GA-04 evidence manifest and reconciliation snapshot PASS'
Write-Step 'GA-04 PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05'
[pscustomobject]$manifest | Format-List
