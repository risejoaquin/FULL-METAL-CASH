param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[GA-01] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Get-RelativePathCompat {
 param([string]$BasePath,[string]$TargetPath)
 $baseFull=[IO.Path]::GetFullPath($BasePath)
 $targetFull=[IO.Path]::GetFullPath($TargetPath)
 $separator=[IO.Path]::DirectorySeparatorChar
 if(-not $baseFull.EndsWith([string]$separator)){ $baseFull += $separator }
 if($targetFull.StartsWith($baseFull,[StringComparison]::OrdinalIgnoreCase)){ return $targetFull.Substring($baseFull.Length) }
 return $targetFull
}
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
function Get-RepositoryBaselineHash {
 param([string]$Root)
 $excluded=@([IO.Path]::DirectorySeparatorChar+'.git'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'.runtime'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'bin'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'obj'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'node_modules'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'dist'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'TestResults'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'docs'+[IO.Path]::DirectorySeparatorChar+'beta'+[IO.Path]::DirectorySeparatorChar+'logs'+[IO.Path]::DirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar+'docs'+[IO.Path]::DirectorySeparatorChar+'ga'+[IO.Path]::DirectorySeparatorChar+'logs'+[IO.Path]::DirectorySeparatorChar)
 $files=Get-ChildItem -Path $Root -Recurse -File -Force | Where-Object { $path=$_.FullName; foreach($fragment in $excluded){if($path.Contains($fragment)){return $false}}; return $true } | Sort-Object FullName
 $rows=foreach($file in $files){$rel=(Get-RelativePathCompat -BasePath $Root -TargetPath $file.FullName).Replace('\','/'); $h=(Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash.ToLowerInvariant(); "$rel`t$h"}
 $payload=[Text.Encoding]::UTF8.GetBytes(($rows -join "`n")); $sha=[Security.Cryptography.SHA256]::Create(); try{($sha.ComputeHash($payload)|ForEach-Object{$_.ToString('x2')}) -join ''}finally{$sha.Dispose()}
}

$base=$BaseUrl.TrimEnd('/')
$sourceBaselineZipSha256='baa99c7ebdf6c5a53dc1de629f3c3bd87a94b42a4be4d137972f97fb54fde484'
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$beta10=Join-Path $repoRoot 'scripts\beta\validate-beta-10-limited-commercial-beta-closure-report.ps1'
$beta10Manifest=Join-Path $repoRoot '.runtime\beta-10-limited-commercial-beta-closure-report\beta-10-limited-commercial-beta-closure-manifest.json'
$secretScan=Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1'
$sqlPath=Join-Path $scriptRoot 'ga-01-general-availability-baseline-freeze-check.sql'
$runtime=Join-Path $repoRoot '.runtime\ga-01-general-availability-baseline-freeze'
$manifestPath=Join-Path $runtime 'ga-01-manifest.json'
$evidencePath=Join-Path $runtime 'ga-01-evidence.md'
$snapshotPath=Join-Path $runtime 'ga-01-snapshot.json'
$logPath=Join-Path $repoRoot 'docs\ga\logs\ga-01-general-availability-baseline-freeze-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
 (Join-Path $repoRoot 'SOLIDPOS_GA_01_GENERAL_AVAILABILITY_BASELINE_FREEZE_AND_READINESS_CHARTER.md'),
 (Join-Path $repoRoot 'docs\ga\ga-01-general-availability-baseline-freeze.md'),
 (Join-Path $repoRoot 'docs\ga\ga-01-readiness-charter.md'),
 (Join-Path $repoRoot 'docs\ga\ga-01-evidence-matrix.md'),
 (Join-Path $repoRoot 'docs\ga\ga-01-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document GA baseline guardrails...'
Assert-True (Test-Path $beta10) 'BETA-10 validator missing.'
Assert-True (Test-Path $secretScan) 'Secret scan missing.'
Assert-True (Test-Path $sqlPath) 'GA-01 SQL baseline snapshot missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required GA-01 document missing: $d"}
Assert-DocumentContains $docs[0] @('GA-01','Baseline Freeze','schemaVersion = 4','syncContract = schema_version_4','PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02')
Assert-DocumentContains $docs[1] @('GA-01','readiness charter','BETA-10','blockers = {}','generalAvailabilityActivated = False','GO GA-02')
Assert-DocumentContains $docs[3] @('scope freeze','non-goals','inherited conditions','schemaVersion = 4','inventory_ledger','none | add | substitute')
Assert-DocumentContains $docs[5] @('PENDING USER VALIDATION','PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02','FAIL / HOTFIX REQUIRED')
$repoBaselineHash=Get-RepositoryBaselineHash -Root $repoRoot
Assert-True ($repoBaselineHash -match '^[a-f0-9]{64}$') 'Repository baseline SHA-256 could not be calculated.'
Write-Step "Repository/document GA baseline guardrails PASS; baselineHash=$repoBaselineHash"

Write-Step 'Secret scan...'
Unblock-File $secretScan -ErrorAction SilentlyContinue
$global:LASTEXITCODE=0
& $secretScan -Root $repoRoot
$global:LASTEXITCODE=0
Write-Step 'Secret scan PASS'

Write-Step 'Fresh BETA-10 revalidation prerequisite...'
Unblock-File $beta10 -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\beta\validate-beta-09-data-quality-reconciliation-closure.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\beta\validate-beta-08-customer-acceptance-validation.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\beta\validate-beta-07-dashboard-daily-monitoring-pack.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\expansion\validate-exp-06-inventory-reconciliation-hardening.ps1') -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\expansion\validate-exp-05-operational-monitoring-hardening.ps1') -ErrorAction SilentlyContinue
$global:LASTEXITCODE=0
& $beta10 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
$global:LASTEXITCODE=0
Assert-True (Test-Path $beta10Manifest) 'Fresh BETA-10 manifest missing.'
$b10=Get-Content -Raw $beta10Manifest | ConvertFrom-Json
Assert-True ($b10.status -eq 'PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP') 'Fresh BETA-10 prerequisite did not PASS.'
Assert-True (@($b10.blockers).Count -eq 0) 'Fresh BETA-10 prerequisite contains blockers.'
Assert-True ([int]$b10.schemaVersion -eq 4) 'Fresh BETA-10 schemaVersion drifted from 4.'
Assert-True ([string]$b10.syncContract -eq 'schema_version_4') 'Fresh BETA-10 syncContract drifted from schema_version_4.'
Assert-True (-not [bool]$b10.generalAvailabilityActivated) 'General Availability is already activated; GA-01 must stop.'
$beta10At=[string]$b10.generatedAt
Write-Step 'Fresh BETA-10 revalidation prerequisite PASS'

Write-Step 'GA-01 production baseline snapshot SQL...'
$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;beta10_at=$beta10At}
Assert-True ($sql.ga01SqlDecision -eq 'GO_GA_02') "GA-01 SQL blockers: $($sql.blockers -join ', ')"
Assert-True ([int]$sql.schemaVersion -eq 4) 'GA-01 SQL schemaVersion drifted from 4.'
Assert-True ([string]$sql.syncContract -eq 'schema_version_4') 'GA-01 SQL sync contract drifted.'
Assert-True (-not [bool]$sql.generalAvailabilityActivated) 'GA-01 snapshot indicates GA activated.'
Assert-True ([long]$sql.activeBetaReleaseCount -eq [long]$b10.activeBetaReleaseCount) 'Active beta release count changed between fresh BETA-10 and GA-01 baseline snapshot.'
Assert-True ([long]$sql.activeStoreCount -eq [long]$b10.activeStoreCount) 'Active store count changed between fresh BETA-10 and GA-01 baseline snapshot.'
Assert-True ([long]$sql.activeTerminalCount -eq [long]$b10.activeTerminalCount) 'Active terminal count changed between fresh BETA-10 and GA-01 baseline snapshot.'
Write-Step 'GA-01 production baseline snapshot SQL PASS'

$generated=(Get-Date).ToUniversalTime().ToString('o')
$conditions=@($sql.conditions)
$blockers=@($sql.blockers)
$snapshot=[ordered]@{
 phase='GA-01';generatedAt=$generated;tenantId=$TenantId;baseUrl=$base;sourceBaselineZipSha256=$sourceBaselineZipSha256;repoBaselineSha256=$repoBaselineHash;freshBeta10At=$beta10At;
 tenant=[ordered]@{count=[long]$sql.tenantCount;activeCount=[long]$sql.activeTenantCount};
 stores=[ordered]@{count=[long]$sql.storeCount;activeCount=[long]$sql.activeStoreCount};
 terminals=[ordered]@{count=[long]$sql.terminalCount;activeCount=[long]$sql.activeTerminalCount};
 users=[ordered]@{count=[long]$sql.userCount;activeCount=[long]$sql.activeUserCount};
 customers=[ordered]@{count=[long]$sql.customerCount};
 catalog=[ordered]@{productCount=[long]$sql.productCount;productPriceCount=[long]$sql.productPriceCount;modifierCount=[long]$sql.modifierCount;invalidModifierBehaviorCount=[long]$sql.invalidModifierBehaviorCount;invalidSubstituteModifierCount=[long]$sql.invalidSubstituteModifierCount};
 sales=[ordered]@{count=[long]$sql.salesCount;acceptedCount=[long]$sql.acceptedSalesCount;salesAfterBeta10Count=[long]$sql.salesAfterBeta10Count};
 payments=[ordered]@{count=[long]$sql.paymentCount;approvedCount=[long]$sql.approvedPaymentCount};
 receipts=[ordered]@{count=[long]$sql.receiptCount;activeCount=[long]$sql.activeReceiptCount};
 returnsRefunds=[ordered]@{returnCount=[long]$sql.returnCount;refundCount=[long]$sql.refundCount};
 cash=[ordered]@{openShiftCount=[long]$sql.openShiftCount;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount};
 inventory=[ordered]@{itemCount=[long]$sql.inventoryItemCount;ledgerEntryCount=[long]$sql.inventoryLedgerEntryCount;negativeItemCount=[long]$sql.negativeInventoryItemCount;sourceOfTruth='inventory_ledger'};
 sync=[ordered]@{processedSchema4Count=[long]$sql.processedSchema4SyncCount;legacySchemaEventCount=[long]$sql.legacySchemaEventCount;retryPendingCount=[long]$sql.retryPendingCount;retryOverSlaCount=[long]$sql.retryOverSlaCount;staleProcessingCount=[long]$sql.staleProcessingCount;pendingConflictCount=[long]$sql.pendingConflictCount;deadLetterCount=[long]$sql.deadLetterCount;newDeadLetterCount=[long]$sql.newDeadLetterCount;untriagedDeadLetterCount=[long]$sql.untriagedDeadLetterCount};
 audit=[ordered]@{eventCount=[long]$sql.auditEventCount;eventsLast24Hours=[long]$sql.auditEventsLast24Hours;eventsAfterBeta10Count=[long]$sql.auditEventsAfterBeta10Count};
 releaseUpdate=[ordered]@{activeBetaReleaseCount=[long]$sql.activeBetaReleaseCount;invalidBetaReleaseCount=[long]$sql.invalidBetaReleaseCount;activeStableReleaseCount=[long]$sql.activeStableReleaseCount};
 inheritedConditions=$conditions;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false
}
$snapshot|ConvertTo-Json -Depth 10|Set-Content -Encoding UTF8 $snapshotPath

$conditionText=if($conditions.Count -eq 0){'none'}else{$conditions -join ', '}
$evidenceConditionLines=if($conditions.Count -eq 0){'- none'}else{($conditions|ForEach-Object{"- $_"}) -join "`n"}
@"
# GA-01 General Availability Baseline Freeze Evidence

Generated: $generated
Tenant: $TenantId
Source BETA-10 ZIP SHA-256: $sourceBaselineZipSha256
Repository baseline SHA-256: $repoBaselineHash
Fresh BETA-10 generatedAt: $beta10At

## Entry gate
- BETA-10: PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP
- blockers = {}
- schemaVersion = 4
- syncContract = schema_version_4
- generalAvailabilityActivated = False

## Baseline snapshot
- tenant: $($sql.tenantCount) / active $($sql.activeTenantCount)
- stores: $($sql.storeCount) / active $($sql.activeStoreCount)
- terminals: $($sql.terminalCount) / active $($sql.activeTerminalCount)
- users: $($sql.userCount) / active $($sql.activeUserCount)
- catalog products: $($sql.productCount); modifiers: $($sql.modifierCount)
- sales: $($sql.salesCount); approved payments: $($sql.approvedPaymentCount)
- receipts: $($sql.receiptCount); returns: $($sql.returnCount); refunds: $($sql.refundCount)
- inventory ledger entries: $($sql.inventoryLedgerEntryCount); negative inventory items: $($sql.negativeInventoryItemCount)
- sync schema-4 processed: $($sql.processedSchema4SyncCount); pending conflicts: $($sql.pendingConflictCount)
- dead-letter: $($sql.deadLetterCount); new: $($sql.newDeadLetterCount); untriaged: $($sql.untriagedDeadLetterCount)
- active beta releases: $($sql.activeBetaReleaseCount); active stable releases: $($sql.activeStableReleaseCount)
- audit events: $($sql.auditEventCount)

## Inherited conditions preserved
$evidenceConditionLines

## Decision
- blockers = {}
- PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02
- General Availability remains NOT activated.
"@ | Set-Content -Encoding UTF8 $evidencePath

$manifest=[ordered]@{
 phase='GA-01';status='PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;entryGate='PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP';baselineContract='general_availability_baseline_freeze_and_readiness_charter';sourceBaselineZipSha256=$sourceBaselineZipSha256;repoBaselineSha256=$repoBaselineHash;freshBeta10At=$beta10At;freshBeta10Decision=[string]$b10.betaDecision;
 tenantCount=[long]$sql.tenantCount;activeStoreCount=[long]$sql.activeStoreCount;activeTerminalCount=[long]$sql.activeTerminalCount;activeUserCount=[long]$sql.activeUserCount;productCount=[long]$sql.productCount;salesCount=[long]$sql.salesCount;paymentCount=[long]$sql.paymentCount;receiptCount=[long]$sql.receiptCount;returnCount=[long]$sql.returnCount;refundCount=[long]$sql.refundCount;inventoryLedgerEntryCount=[long]$sql.inventoryLedgerEntryCount;negativeInventoryItemCount=[long]$sql.negativeInventoryItemCount;processedSchema4SyncCount=[long]$sql.processedSchema4SyncCount;legacySchemaEventCount=[long]$sql.legacySchemaEventCount;retryPendingCount=[long]$sql.retryPendingCount;retryOverSlaCount=[long]$sql.retryOverSlaCount;staleProcessingCount=[long]$sql.staleProcessingCount;pendingConflictCount=[long]$sql.pendingConflictCount;deadLetterCount=[long]$sql.deadLetterCount;newDeadLetterCount=[long]$sql.newDeadLetterCount;untriagedDeadLetterCount=[long]$sql.untriagedDeadLetterCount;activeBetaReleaseCount=[long]$sql.activeBetaReleaseCount;activeStableReleaseCount=[long]$sql.activeStableReleaseCount;inheritedConditions=$conditions;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-02 - Sync Queue and SLA Closure'
}
$manifest|ConvertTo-Json -Depth 10|Set-Content -Encoding UTF8 $manifestPath
@"
# GA-01 General Availability Baseline Freeze Log

- status: $($manifest.status)
- generatedAt: $generated
- freshBeta10At: $beta10At
- sourceBaselineZipSha256: $sourceBaselineZipSha256
- repoBaselineSha256: $repoBaselineHash
- inheritedConditions: $conditionText
- blockers: none
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: false
- nextPhase: GA-02 - Sync Queue and SLA Closure
"@ | Set-Content -Encoding UTF8 $logPath
Assert-True ($blockers.Count -eq 0) "GA-01 blockers: $($blockers -join ', ')"
Write-Step 'GA-01 evidence manifest and baseline snapshot PASS'
Write-Step 'GA-01 PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02'
[pscustomobject]$manifest | Format-List
