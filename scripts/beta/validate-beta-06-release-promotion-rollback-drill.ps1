param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = 'Stop'
function Write-Step { param([string]$Message) Write-Host "[BETA-06] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){ throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($term in $Terms){Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path missing term: $term"} }

$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$exp09Validator=Join-Path $repoRoot 'scripts\expansion\validate-exp-09-release-management-update-channel.ps1'
$sqlCheck=Join-Path $scriptRoot 'beta-06-release-promotion-rollback-check.sql'
$sqlRollback=Join-Path $scriptRoot 'beta-06-release-rollback-transaction-drill.sql'
$runtime=Join-Path $repoRoot '.runtime\beta-06-release-promotion-rollback-drill'
$manifestPath=Join-Path $runtime 'beta-06-release-promotion-rollback-manifest.json'
$evidencePath=Join-Path $runtime 'beta-06-release-promotion-evidence.json'
$releaseNotesPath=Join-Path $runtime 'beta-06-release-notes.md'
$exp09ManifestPath=Join-Path $repoRoot '.runtime\exp-09-release-management-update-channel\release-management-update-channel-manifest.json'
$docs=@(
  (Join-Path $repoRoot 'SOLIDPOS_BETA_06_BETA_RELEASE_PROMOTION_AND_ROLLBACK_DRILL.md'),
  (Join-Path $repoRoot 'docs\beta\beta-06-release-promotion-rollback-drill.md'),
  (Join-Path $repoRoot 'docs\beta\beta-06-release-promotion-checklist.md'),
  (Join-Path $repoRoot 'docs\beta\beta-06-rollback-runbook.md'),
  (Join-Path $repoRoot 'docs\beta\beta-06-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null

Write-Step 'Repository/document guardrails...'
Assert-True (Test-Path $exp09Validator) 'EXP-09 release validator missing.'
Assert-True (Test-Path $sqlCheck) 'BETA-06 SQL cross-check missing.'
Assert-True (Test-Path $sqlRollback) 'BETA-06 rollback transaction drill missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($doc in $docs){ Assert-True (Test-Path $doc) "Required BETA-06 document missing: $doc" }
Assert-DocumentContains $docs[0] @('BETA-06','internal','beta','rollback','GO BETA-07')
Assert-DocumentContains $docs[3] @('rollback','transaction','destructive delete','revoked_at')
Write-Step 'Repository/document guardrails PASS'

Write-Step 'Unblock inherited release validator...'
Unblock-File $exp09Validator -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -ErrorAction SilentlyContinue
Write-Step 'Unblock inherited release validator PASS'

Write-Step 'Execute hardened release-management contract...'
& $exp09Validator -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
if($LASTEXITCODE -ne 0){throw "EXP-09 inherited validator failed with exit code $LASTEXITCODE."}
Assert-True (Test-Path $exp09ManifestPath) 'EXP-09 manifest was not created.'
$source=Get-Content -Raw $exp09ManifestPath | ConvertFrom-Json
Assert-True ($source.status -eq 'PASS RELEASE MANAGEMENT AND UPDATE CHANNEL / GO EXP-10') 'EXP-09 source contract did not PASS.'
Assert-True ($source.releaseChannel -eq 'internal') 'Source release must be internal.'
Assert-True ($source.mandatory -eq $false) 'Source release must be non-mandatory.'
Assert-True ($source.universalInstaller -eq $true) 'Source release must be universal Velopack.'
Assert-True ($source.signaturePresent -eq $true) 'Source release signature missing.'
Write-Step 'Execute hardened release-management contract PASS'

Write-Step 'Authenticate and promote internal release to beta...'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
$headers=@{Authorization="Bearer $($session.accessToken)"}
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Login did not return access token.'
$sourceReleaseId=[string]$source.releaseId
$sourceReleaseList=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($source.rollbackVersion))&channel=internal&packageType=velopack" -Headers $headers -TimeoutSec 30
Assert-True ($sourceReleaseList.release.id -eq $sourceReleaseId) 'Internal update check did not resolve source release.'
$promoteBody=@{
  version=[string]$source.releaseVersion
  channel='beta'
  packageType='velopack'
  artifactUrl=[string]$sourceReleaseList.release.artifactUrl
  artifactHash=[string]$source.artifactHash
  signature=[string]$sourceReleaseList.release.signature
  rollbackVersion=[string]$source.rollbackVersion
  mandatory=$false
  universalInstaller=$true
  tenantScoped=$true
} | ConvertTo-Json
$promoted=Invoke-RestMethod -Method Post -Uri "$base/api/v1/updates/releases" -Headers $headers -ContentType 'application/json' -Body $promoteBody -TimeoutSec 30
Assert-True ($promoted.channel -eq 'beta') 'Promoted release channel must be beta.'
Assert-True ($promoted.version -eq $source.releaseVersion) 'Promotion changed release version.'
Assert-True ($promoted.artifactHash -eq $source.artifactHash) 'Promotion changed artifact hash.'
Assert-True ($promoted.signature -eq $sourceReleaseList.release.signature) 'Promotion changed signature.'
Assert-True ($promoted.mandatory -eq $false) 'Beta promotion must not be mandatory.'
Assert-True ($promoted.universalInstaller -eq $true) 'Beta promotion must remain universal Velopack.'
Assert-True ($promoted.tenantId -eq $TenantId) 'Beta promotion must remain tenant-scoped.'
Write-Step 'Authenticate and promote internal release to beta PASS'

Write-Step 'Beta update check and release notes...'
$betaCheck=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($source.rollbackVersion))&channel=beta&packageType=velopack" -Headers $headers -TimeoutSec 30
Assert-True ($betaCheck.updateAvailable -eq $true) 'Beta update check did not report update available.'
Assert-True ($betaCheck.release.id -eq $promoted.id) 'Beta update check did not return promoted release.'
Assert-True ($betaCheck.release.mandatory -eq $false) 'Beta update check returned mandatory release.'
@"
# BETA-06 Release Notes

- Version: $($promoted.version)
- Source channel: internal
- Promoted channel: beta
- Package: Velopack universal installer
- Tenant scoped: true
- Mandatory: false
- Artifact SHA-256: $($promoted.artifactHash)
- Signature present: true
- Rollback version: $($promoted.rollbackVersion)
- Promotion source release: $sourceReleaseId
- Promoted release: $($promoted.id)
"@ | Set-Content -Encoding UTF8 $releaseNotesPath
Write-Step 'Beta update check and release notes PASS'

Write-Step 'SQL release promotion cross-check...'
$sql=Invoke-DbJsonFile $sqlCheck @{tenant_id=$TenantId;source_release_id=$sourceReleaseId;promoted_release_id=[string]$promoted.id;release_version=[string]$promoted.version}
Assert-True ($sql.beta06SqlDecision -eq 'GO') "BETA-06 SQL blockers: $($sql.blockers -join ', ')"
Write-Step 'SQL release promotion cross-check PASS'

Write-Step 'Rollback transaction drill without persistent production mutation...'
$rollback=Invoke-DbJsonFile $sqlRollback @{tenant_id=$TenantId;promoted_release_id=[string]$promoted.id}
Assert-True ($rollback.rollbackValidation -eq 'GO') 'Rollback transaction drill returned NO-GO.'
Assert-True ($rollback.promotedReleaseActiveAfterRollback -eq $true) 'Promoted release was not restored by transaction rollback.'
Assert-True ([long]$rollback.persistedRollbackMutationCount -eq 0) 'Rollback drill persisted a revoke mutation.'
$betaCheckAfter=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($source.rollbackVersion))&channel=beta&packageType=velopack" -Headers $headers -TimeoutSec 30
Assert-True ($betaCheckAfter.release.id -eq $promoted.id) 'Promoted beta release was not active after rollback drill.'
Write-Step 'Rollback transaction drill without persistent production mutation PASS'

Write-Step 'Build BETA-06 evidence manifest...'
$blockers=@($sql.blockers)
$conditions=@()
if([string]::IsNullOrWhiteSpace([string]$promoted.rollbackVersion)){ $conditions += 'rollback_version_review_required' }
$evidence=[ordered]@{
  sourceReleaseId=$sourceReleaseId; promotedReleaseId=[string]$promoted.id; version=[string]$promoted.version
  sourceChannel='internal'; targetChannel='beta'; artifactUrl=[string]$promoted.artifactUrl; artifactHash=[string]$promoted.artifactHash
  signaturePresent=(-not [string]::IsNullOrWhiteSpace([string]$promoted.signature)); rollbackVersion=[string]$promoted.rollbackVersion
  mandatory=[bool]$promoted.mandatory; universalInstaller=[bool]$promoted.universalInstaller; tenantScoped=($promoted.tenantId -eq $TenantId)
  updateCheckDecision=[string]$betaCheck.decision; rollbackValidation=[string]$rollback.rollbackValidation
  persistedRollbackMutationCount=[long]$rollback.persistedRollbackMutationCount; releaseNotesPath=$releaseNotesPath
}
$evidence | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $evidencePath
$manifest=[ordered]@{
 phase='BETA-06'; status='PASS BETA RELEASE PROMOTION ROLLBACK DRILL / GO BETA-07'; tenantId=$TenantId; baseUrl=$base
 generatedAt=(Get-Date).ToUniversalTime().ToString('o'); betaDecision='GO_BETA_07'
 sourceReleaseId=$sourceReleaseId; promotedReleaseId=[string]$promoted.id; releaseVersion=[string]$promoted.version
 sourceChannel='internal'; promotedChannel='beta'; packageType='velopack'; artifactHash=[string]$promoted.artifactHash
 signaturePresent=$evidence.signaturePresent; rollbackVersion=[string]$promoted.rollbackVersion; mandatory=[bool]$promoted.mandatory
 universalInstaller=[bool]$promoted.universalInstaller; tenantScoped=$evidence.tenantScoped; updateAvailable=[bool]$betaCheck.updateAvailable
 rollbackValidation=[string]$rollback.rollbackValidation; persistedRollbackMutationCount=[long]$rollback.persistedRollbackMutationCount
 promotionArtifactMatchCount=[long]$sql.promotionArtifactMatchCount; releaseAuditCount=[long]$sql.releaseAuditCount
 blockers=$blockers; conditions=$conditions; schemaVersion=4; syncContract='schema_version_4'
 nextPhase='BETA-07 - Beta Dashboard and Daily Monitoring Pack'
}
Assert-True ($blockers.Count -eq 0) "BETA-06 blockers: $($blockers -join ', ')"
$manifest | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $manifestPath
Write-Step 'BETA-06 evidence manifest PASS'
Write-Step 'BETA-06 PASS BETA RELEASE PROMOTION ROLLBACK DRILL / GO BETA-07'
[pscustomobject]$manifest
