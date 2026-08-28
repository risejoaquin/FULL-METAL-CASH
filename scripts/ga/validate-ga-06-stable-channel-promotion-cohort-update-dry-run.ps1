param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [string]$ReleaseVersion='1.0.0-rc.1',
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[GA-06] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','terminals')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try {[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)} finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)} }
function Invoke-DbJsonFile {
 param([string]$SqlPath,[hashtable]$Variables)
 $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path
 $name=Split-Path -Leaf $SqlPath
 $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1')
 foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}
 $args+=@('-f',"/sql/$name")
 $global:LASTEXITCODE=0
 $out=docker @args
 if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}
 $global:LASTEXITCODE=0
 $rows=@($out | ForEach-Object { ([string]$_).Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
 for($i=$rows.Count-1; $i -ge 0; $i--){
   $candidate=$rows[$i]
   if($candidate -match '^(BEGIN|COMMIT|ROLLBACK|SET)$'){ continue }
   try {
     $parsed=$candidate | ConvertFrom-Json -ErrorAction Stop
     if($null -ne $parsed){ return $parsed }
   } catch { }
 }
 throw "DB JSON file did not return a valid JSON object: $SqlPath."
}
function Invoke-DbScalar { param([string]$Sql) $global:LASTEXITCODE=0; $out=docker run --rm --env "DATABASE_URL=$DatabaseUrl" postgres:17 psql "$DatabaseUrl" -tA -v ON_ERROR_STOP=1 -c $Sql; if($LASTEXITCODE -ne 0){throw 'DB scalar command failed.'}; $global:LASTEXITCODE=0; $rows=@($out | ForEach-Object { [string]$_ }); if($rows.Count -eq 0){ return '' }; $last=$rows | Select-Object -Last 1; if($null -eq $last){ return '' }; return ([string]$last).Trim() }

$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$secretScan=Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1'
$ga04=Join-Path $scriptRoot 'validate-ga-04-production-data-integrity-financial-reconciliation.ps1'
$ga04Manifest=Join-Path $repoRoot '.runtime\ga-04-production-data-integrity-financial-reconciliation\ga-04-manifest.json'
$ga05Baseline=Join-Path $repoRoot 'docs\ga\ga-05-real-production-baseline.json'
$chainSql=Join-Path $scriptRoot 'ga-06-release-chain-state.sql'
$checkSql=Join-Path $scriptRoot 'ga-06-stable-channel-promotion-cohort-update-dry-run-check.sql'
$rollbackSql=Join-Path $scriptRoot 'ga-06-stable-release-rollback-transaction-drill.sql'
$runtime=Join-Path $repoRoot '.runtime\ga-06-stable-channel-promotion-cohort-update-dry-run'
$manifestPath=Join-Path $runtime 'ga-06-manifest.json'
$evidencePath=Join-Path $runtime 'ga-06-evidence.md'
$snapshotPath=Join-Path $runtime 'ga-06-snapshot.json'
$releaseNotesPath=Join-Path $runtime 'ga-06-release-notes.md'
$logPath=Join-Path $repoRoot 'docs\ga\logs\ga-06-stable-channel-promotion-cohort-update-dry-run-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
 (Join-Path $repoRoot 'SOLIDPOS_GA_06_STABLE_CHANNEL_PROMOTION_AND_COHORT_UPDATE_DRY_RUN.md'),
 (Join-Path $repoRoot 'docs\ga\ga-06-stable-channel-promotion-cohort-update-dry-run.md'),
 (Join-Path $repoRoot 'docs\ga\ga-06-cohort-targeting-contract.md'),
 (Join-Path $repoRoot 'docs\ga\ga-06-promotion-and-rollback-runbook.md'),
 (Join-Path $repoRoot 'docs\ga\ga-06-go-no-go.md')
)
Remove-Item -Recurse -Force $runtime -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $runtime,(Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document GA-06 guardrails...'
Assert-True (Test-Path $secretScan) 'Secret scan missing.'
Assert-True (Test-Path $ga04) 'GA-04 validator missing.'
Assert-True (Test-Path $ga05Baseline) 'GA-05 real production baseline missing.'
Assert-True (Test-Path $chainSql) 'GA-06 release chain SQL missing.'
Assert-True (Test-Path $checkSql) 'GA-06 final SQL missing.'
Assert-True (Test-Path $rollbackSql) 'GA-06 rollback drill SQL missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required GA-06 document missing: $d"}
Assert-DocumentContains $docs[0] @('GA-06','Stable Channel Promotion and Cohort Update Dry Run','activeStableReleaseCount >= 1','mandatory = False','tenantScoped = True','updateAvailable = True','promotionArtifactMatchCount >= 1','releaseAuditCount >= 1')
Assert-DocumentContains $docs[1] @('PENDING USER VALIDATION','internal -> beta -> stable','cohort','targetTerminalIds','public rollout','GA-07')
Assert-DocumentContains $docs[2] @('stable','cohort','update check','rollback','audit','schema version 4')
Assert-DocumentContains $docs[3] @('targetTerminalIds','terminalId','outside cohort','backward compatible')
Assert-DocumentContains $docs[4] @('rollback','transaction','no destructive delete','stable')
Assert-DocumentContains $docs[5] @('PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07','FAIL / HOTFIX REQUIRED')
Write-Step 'Repository/document GA-06 guardrails PASS'

Write-Step 'Secret scan...'
& $secretScan -Root $repoRoot
Write-Step 'Secret scan PASS'

Write-Step 'Fresh GA-04 prerequisite revalidation...'
Unblock-File $ga04 -ErrorAction SilentlyContinue
& $ga04 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
Assert-True (Test-Path $ga04Manifest) 'Fresh GA-04 manifest missing.'
$ga04State=Get-Content -Raw $ga04Manifest | ConvertFrom-Json
Assert-True ($ga04State.status -eq 'PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05') 'Fresh GA-04 prerequisite did not PASS.'
Write-Step 'Fresh GA-04 prerequisite revalidation PASS'

Write-Step 'GA-05 real production evidence gate...'
$ga05=Get-Content -Raw $ga05Baseline | ConvertFrom-Json
Assert-True ($ga05.status -eq 'PASS REAL PRODUCTION') 'GA-05 baseline is not PASS REAL PRODUCTION.'
Assert-True ($ga05.releaseCandidateVersion -eq $ReleaseVersion) 'ReleaseVersion does not match GA-05 validated RC.'
Assert-True ($ga05.signatureValid -eq $true) 'GA-05 signature evidence is not valid.'
Assert-True ($ga05.reproducibleBuild -eq $true) 'GA-05 reproducible build evidence missing.'
Assert-True ($ga05.schemaVersion -eq 4 -and $ga05.syncContract -eq 'schema_version_4') 'GA-05 schema/sync contract drift.'
Write-Step 'GA-05 real production evidence gate PASS'

Write-Step 'Production cohort-targeting migration preflight...'
$targetTable=Invoke-DbScalar "SELECT CASE WHEN to_regclass('pos.update_release_targets') IS NOT NULL THEN 'present' ELSE 'missing' END;"
Assert-True ($targetTable -eq 'present') 'pos.update_release_targets is missing. Apply migration 019 before GA-06.'
Write-Step 'Production cohort-targeting migration preflight PASS'

Write-Step 'Authenticate, select reduced controlled cohort and compatible outside client...'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Login did not return access token.'
$headers=@{Authorization="Bearer $($session.accessToken)"}
$terminalResponse=Invoke-RestMethod -Method Get -Uri "$base/api/v1/terminals" -Headers $headers -TimeoutSec 30
$terminals=Get-Items $terminalResponse
$active=@($terminals | Where-Object { [string]$_.status -eq 'active' })
$existingTargetId=Invoke-DbScalar "SELECT coalesce((SELECT rt.terminal_id::text FROM pos.update_release_targets rt JOIN pos.update_releases r ON r.id=rt.release_id WHERE r.tenant_id='$TenantId'::uuid AND r.version='$ReleaseVersion' AND r.package_type='velopack' AND r.channel IN ('internal','beta','stable') ORDER BY r.published_at LIMIT 1),'');"
if(-not [string]::IsNullOrWhiteSpace($existingTargetId)){
  $target=@($active | Where-Object { [string]$_.id -eq $existingTargetId } | Select-Object -First 1)
} else {
  $target=@($active | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.appVersion) -and ([string]$_.fingerprint -match '^(pilot|iteration|beta|exp|ga)-') } | Select-Object -First 1)
}
Assert-True ($target.Count -eq 1) 'No controlled active validation terminal with appVersion is available for GA-06 cohort.'
$targetTerminal=$target[0]
$outside=@($active | Where-Object { $_.id -ne $targetTerminal.id } | Select-Object -First 1)
Assert-True ($outside.Count -eq 1) 'GA-06 requires at least one active terminal outside the reduced cohort.'
$outsideTerminal=$outside[0]
Write-Step "Reduced cohort selected; target=$($targetTerminal.id); outside=$($outsideTerminal.id)"

$artifactHash=[string]$ga05.artifactHash
$rollbackVersion=[string]$ga05.rollbackVersion
$signature="AUTHENTICODE:$($ga05.signingIdentityType):$($ga05.signerThumbprint)"
$artifactUrl="https://updates.solidpos.local/ga-05/$ReleaseVersion/$($ga05.artifactFile)"
$vars=@{tenant_id=$TenantId;release_version=$ReleaseVersion;artifact_hash=$artifactHash;artifact_url=$artifactUrl;signature=$signature;rollback_version=$rollbackVersion;target_terminal_id=[string]$targetTerminal.id}

Write-Step 'Pre-promotion chain drift check...'
$state=Invoke-DbJsonFile $chainSql $vars
if([long]$state.mismatchedReleaseCount -ne 0){
  Write-Host '[GA-06] Existing release identity drift diagnostics:'
  foreach($d in @($state.releaseIdentityDiagnostics)){
    $mismatches=@()
    if(-not [bool]$d.artifactUrlMatch){$mismatches+='artifactUrl'}
    if(-not [bool]$d.artifactHashMatch){$mismatches+='artifactHash'}
    if(-not [bool]$d.signatureMatch){$mismatches+='signature'}
    if(-not [bool]$d.rollbackVersionMatch){$mismatches+='rollbackVersion'}
    if(-not [bool]$d.mandatoryMatch){$mismatches+='mandatory'}
    if(-not [bool]$d.universalInstallerMatch){$mismatches+='universalInstaller'}
    if(-not [bool]$d.tenantScopeMatch){$mismatches+='tenantScope'}
    if(-not [bool]$d.activeMatch){$mismatches+='revokedAt'}
    if($mismatches.Count -gt 0){
      Write-Host ("[GA-06] channel={0}; releaseId={1}; mismatches={2}; rollbackActual={3}; rollbackExpected={4}" -f $d.channel,$d.id,($mismatches -join ','),$d.rollbackVersionActual,$d.rollbackVersionExpected)
    }
  }
  throw 'An existing GA-06 release chain row conflicts with the validated GA-05 release identity. No promotion or target reconciliation was performed.'
}
Assert-True ([long]$state.unexpectedTargetCount -eq 0) 'An existing GA-06 chain targets terminals outside the selected cohort.'
$targetActive=Invoke-DbScalar "SELECT count(*)::text FROM pos.terminals WHERE tenant_id='$TenantId'::uuid AND id='$($targetTerminal.id)'::uuid AND status='active';"
Assert-True ([long]$targetActive -eq 1) 'Selected GA-06 target terminal is not active in PostgreSQL source-of-truth.'
Write-Step 'Pre-promotion chain drift check PASS'

function Ensure-Ga06Release {
 param([string]$Channel,[string]$ExistingId)
 $payload=@{version=$ReleaseVersion;channel=$Channel;packageType='velopack';artifactUrl=$artifactUrl;artifactHash=$artifactHash;signature=$signature;rollbackVersion=$rollbackVersion;mandatory=$false;universalInstaller=$true;tenantScoped=$true;targetTerminalIds=@([string]$targetTerminal.id)}
 $body=$payload | ConvertTo-Json -Depth 6
 Write-Host ("[GA-06] Release request contract: channel={0}; version={1}; tenantScoped={2}; mandatory={3}; universalInstaller={4}; targetCount={5}; artifactUrlPresent={6}; artifactHashLength={7}; signatureLength={8}; rollbackVersionPresent={9}" -f $Channel,$ReleaseVersion,$payload.tenantScoped,$payload.mandatory,$payload.universalInstaller,@($payload.targetTerminalIds).Count,(-not [string]::IsNullOrWhiteSpace($payload.artifactUrl)),([string]$payload.artifactHash).Length,([string]$payload.signature).Length,(-not [string]::IsNullOrWhiteSpace($payload.rollbackVersion)))
 try {
   $created=Invoke-RestMethod -Method Post -Uri "$base/api/v1/updates/releases" -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 30
 } catch {
   $responseBody=''
   try {
     if($null -ne $_.Exception.Response){
       $stream=$_.Exception.Response.GetResponseStream()
       if($null -ne $stream){
         $reader=New-Object System.IO.StreamReader($stream)
         $responseBody=$reader.ReadToEnd()
         $reader.Dispose()
       }
     }
   } catch {}
   if(-not [string]::IsNullOrWhiteSpace($responseBody)){
     Write-Host ("[GA-06] Release reconciliation HTTP failure body: {0}" -f $responseBody)
   }
   throw
 }
 Assert-True ($created.channel -eq $Channel) "Created/reconciled release channel mismatch for $Channel."
 Assert-True ($created.version -eq $ReleaseVersion) "Created/reconciled release version mismatch for $Channel."
 Assert-True ($created.artifactHash -eq $artifactHash) "Created/reconciled release artifact hash mismatch for $Channel."
 Assert-True ($created.mandatory -eq $false -and $created.universalInstaller -eq $true) "Created/reconciled release safety contract invalid for $Channel."
 Assert-True ($created.tenantId -eq $TenantId) "Created/reconciled release is not tenant scoped for $Channel."
 if(-not [string]::IsNullOrWhiteSpace($ExistingId)){
   Assert-True ([string]$created.id -eq $ExistingId) "Existing release identity changed during idempotent reconciliation for $Channel."
 }
 return [string]$created.id
}

Write-Step 'Promote identical RC through internal -> beta -> stable for one controlled terminal...'
$internalId=Ensure-Ga06Release 'internal' ([string]$state.internalReleaseId)
$state=Invoke-DbJsonFile $chainSql $vars
Assert-True ([long]$state.internalTargetCount -eq 1) 'Internal release target was not persisted exactly once after idempotent reconciliation.'
Assert-True ([long]$state.targetTenantMismatchCount -eq 0) 'Internal release target tenant consistency failed.'
$betaId=Ensure-Ga06Release 'beta' ([string]$state.betaReleaseId)
$state=Invoke-DbJsonFile $chainSql $vars
Assert-True ([long]$state.betaTargetCount -eq 1) 'Beta release target was not persisted exactly once.'
Assert-True ([long]$state.targetTenantMismatchCount -eq 0) 'Beta release target tenant consistency failed.'
$stableId=Ensure-Ga06Release 'stable' ([string]$state.stableReleaseId)
$state=Invoke-DbJsonFile $chainSql $vars
Assert-True ([long]$state.activeChainReleaseCount -eq 3) 'Internal/beta/stable promotion chain is incomplete.'
Assert-True ([long]$state.expectedTargetCount -eq 3 -and [long]$state.internalTargetCount -eq 1 -and [long]$state.betaTargetCount -eq 1 -and [long]$state.stableTargetCount -eq 1) 'Each promoted release must target exactly the controlled cohort terminal once.'
Assert-True ([long]$state.targetTenantMismatchCount -eq 0) 'Promotion target tenant consistency failed.'
Assert-True ([long]$state.unexpectedTargetCount -eq 0) 'Promotion targeted a terminal outside the controlled cohort.'
Write-Step 'Promote identical RC through internal -> beta -> stable PASS'

Write-Step 'Targeted update-check matrix...'
$targetStable=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=stable&packageType=velopack&terminalId=$($targetTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($targetStable.updateAvailable -eq $true) 'Target cohort stable update check did not report updateAvailable.'
Assert-True ($targetStable.release.id -eq $stableId) 'Target cohort stable update check returned unexpected release.'
Assert-True ($targetStable.release.mandatory -eq $false) 'Target cohort stable update unexpectedly mandatory.'
$currentStable=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($ReleaseVersion))&channel=stable&packageType=velopack&terminalId=$($targetTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($currentStable.updateAvailable -eq $false) 'Current RC client should not be offered the same stable release.'
$newerStable=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=99.0.0&channel=stable&packageType=velopack&terminalId=$($targetTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($newerStable.updateAvailable -eq $false) 'A client newer than the GA-06 RC must never be offered a downgrade.'
$outsideStable=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=stable&packageType=velopack&terminalId=$($outsideTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($outsideStable.updateAvailable -eq $false) 'Terminal outside cohort was offered the stable RC.'
$unscopedStable=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=stable&packageType=velopack" -Headers $headers -TimeoutSec 30
Assert-True ($unscopedStable.updateAvailable -eq $false) 'Targeted stable RC must not be exposed without terminal identity.'
$targetBeta=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=beta&packageType=velopack&terminalId=$($targetTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($targetBeta.updateAvailable -eq $true -and $targetBeta.release.id -eq $betaId) 'Target cohort beta promotion check failed.'
$targetInternal=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=internal&packageType=velopack&terminalId=$($targetTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($targetInternal.updateAvailable -eq $true -and $targetInternal.release.id -eq $internalId) 'Target cohort internal promotion check failed.'
Write-Step 'Targeted update-check matrix PASS'

@"
# GA-06 Release Notes

- Release candidate: $ReleaseVersion
- Promotion path: internal -> beta -> stable
- Artifact SHA-256: $artifactHash
- GA-05 signer thumbprint: $($ga05.signerThumbprint)
- Signing mode: $($ga05.signingIdentityType)
- Cohort size: 1 controlled terminal
- Target terminal: $($targetTerminal.id)
- Outside terminal negative check: $($outsideTerminal.id)
- Mandatory: false
- Tenant scoped: true
- Rollback version: $rollbackVersion
- Public rollout allowed: false
- Production signing required before public promotion: $($ga05.productionSigningRequiredBeforePublicPromotion)
"@ | Set-Content -Encoding UTF8 $releaseNotesPath

Write-Step 'Rollback transaction dry-run...'
$rollback=Invoke-DbJsonFile $rollbackSql @{tenant_id=$TenantId;stable_release_id=$stableId;rollback_version=$rollbackVersion}
Assert-True ($rollback.rollbackValidation -eq 'GO') 'Stable rollback transaction drill failed.'
$stableAfter=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=stable&packageType=velopack&terminalId=$($targetTerminal.id)" -Headers $headers -TimeoutSec 30
Assert-True ($stableAfter.updateAvailable -eq $true -and $stableAfter.release.id -eq $stableId) 'Stable release did not remain active after rollback transaction drill.'
Write-Step 'Rollback transaction dry-run PASS'

Write-Step 'GA-06 final production SQL gate...'
$sql=Invoke-DbJsonFile $checkSql @{tenant_id=$TenantId;release_version=$ReleaseVersion;artifact_hash=$artifactHash;artifact_url=$artifactUrl;signature=$signature;rollback_version=$rollbackVersion;target_terminal_id=[string]$targetTerminal.id;outside_terminal_id=[string]$outsideTerminal.id}
Assert-True ($sql.ga06SqlDecision -eq 'GO') "GA-06 SQL blockers: $($sql.blockers -join ', ')"
Assert-True ([long]$sql.activeStableReleaseCount -ge 1) 'activeStableReleaseCount must be >= 1.'
Assert-True ([long]$sql.promotionArtifactMatchCount -ge 1) 'promotionArtifactMatchCount must be >= 1.'
Assert-True ([long]$sql.releaseAuditCount -ge 1) 'releaseAuditCount must be >= 1.'
Write-Step 'GA-06 final production SQL gate PASS'

$generated=(Get-Date).ToUniversalTime()
$blockers=@($sql.blockers)
$manifest=[ordered]@{
 phase='GA-06';status='PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated.ToString('o');entryGate='PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06';
 releaseVersion=$ReleaseVersion;promotionPath='internal -> beta -> stable';internalReleaseId=$internalId;betaReleaseId=$betaId;stableReleaseId=$stableId;artifactHash=$artifactHash;artifactIdentity='MATCH_GA05';signatureIdentity=$signature;mandatory=$false;tenantScoped=$true;
 cohortSize=1;targetTerminalId=[string]$targetTerminal.id;targetTerminalFingerprint=[string]$targetTerminal.fingerprint;targetTerminalAppVersion=[string]$targetTerminal.appVersion;outsideTerminalId=[string]$outsideTerminal.id;updateAvailable=$true;outsideCohortUpdateAvailable=$false;unscopedUpdateAvailable=$false;
 rollbackVersion=$rollbackVersion;rollbackValidation=[string]$rollback.rollbackValidation;promotionArtifactMatchCount=[long]$sql.promotionArtifactMatchCount;releaseAuditCount=[long]$sql.releaseAuditCount;cohortAuditCount=[long]$sql.cohortAuditCount;activeStableReleaseCount=[long]$sql.activeStableReleaseCount;
 dryRun=$true;publicRolloutAllowed=$false;productionSigningRequiredBeforePublicPromotion=[bool]$ga05.productionSigningRequiredBeforePublicPromotion;stableChannelPromotionPendingClosed=$true;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-07 - Backup, Restore, Rollback and Disaster Recovery'
}
Assert-True ($blockers.Count -eq 0) "GA-06 blockers: $($blockers -join ', ')"
$manifest | ConvertTo-Json -Depth 14 | Set-Content -Encoding UTF8 $manifestPath
[ordered]@{phase='GA-06';generatedAt=$generated.ToString('o');ga04=$ga04State;ga05Baseline=$ga05;chain=$state;sql=$sql;rollback=$rollback;manifest=$manifest} | ConvertTo-Json -Depth 18 | Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-06 Stable Channel Promotion and Cohort Update Dry Run Evidence

Generated: $($generated.ToString('o'))
Release: $ReleaseVersion
Promotion: internal -> beta -> stable
Target cohort size: 1
Target terminal: $($targetTerminal.id)
Outside terminal: $($outsideTerminal.id)

- activeStableReleaseCount: $($sql.activeStableReleaseCount)
- mandatory: false
- tenantScoped: true
- updateAvailable target cohort: true
- updateAvailable outside cohort: false
- updateAvailable without terminalId: false
- promotionArtifactMatchCount: $($sql.promotionArtifactMatchCount)
- releaseAuditCount: $($sql.releaseAuditCount)
- rollbackValidation: $($rollback.rollbackValidation)
- publicRolloutAllowed: false
- productionSigningRequiredBeforePublicPromotion: $($ga05.productionSigningRequiredBeforePublicPromotion)
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False

PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07
"@ | Set-Content -Encoding UTF8 $evidencePath
@"
# GA-06 Stable Channel Promotion and Cohort Update Dry Run Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- releaseVersion: $ReleaseVersion
- targetTerminalId: $($manifest.targetTerminalId)
- cohortSize: 1
- activeStableReleaseCount: $($manifest.activeStableReleaseCount)
- promotionArtifactMatchCount: $($manifest.promotionArtifactMatchCount)
- releaseAuditCount: $($manifest.releaseAuditCount)
- rollbackValidation: $($manifest.rollbackValidation)
- publicRolloutAllowed: false
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath
Write-Step 'GA-06 evidence manifest and cohort snapshot PASS'
Write-Step 'GA-06 PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07'
[pscustomobject]$manifest
