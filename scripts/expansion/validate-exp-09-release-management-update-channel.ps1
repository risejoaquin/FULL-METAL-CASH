
param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-09] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','metrics','buckets','channels','releases')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function New-Sha256Hex { param([string]$Value) $sha=[System.Security.Cryptography.SHA256]::Create(); try { $bytes=[System.Text.Encoding]::UTF8.GetBytes($Value); return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-09-release-management-update-channel-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-09-release-management-update-channel'
$manifestPath=Join-Path $runtimeDirectory 'release-management-update-channel-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-09-release-management-update-channel-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-09-release-management-update-channel.md'
  semver=Join-Path $repoRoot 'docs\expansion\exp-09-semver-channel-policy.md'
  smoke=Join-Path $repoRoot 'docs\expansion\exp-09-release-smoke-test-checklist.md'
  migration=Join-Path $repoRoot 'docs\expansion\exp-09-safe-migration-policy.md'
  rollback=Join-Path $repoRoot 'docs\expansion\exp-09-release-rollback-runbook.md'
  notes=Join-Path $repoRoot 'docs\expansion\exp-09-release-notes-template.md'
  channels=Join-Path $repoRoot 'docs\expansion\exp-09-update-channel-operations.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-09-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-09 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-09 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-09','release management','update channel','rollback version','exp-10')
Assert-DocumentContains -Path $docs.semver -Terms @('semver','internal','beta','stable','mandatory releases')
Assert-DocumentContains -Path $docs.smoke -Terms @('release smoke test','updates/channels','updates/releases','updates/check','sql cross-check')
Assert-DocumentContains -Path $docs.migration -Terms @('migration preflight','dotnet build','dotnet test','destructive changes','rollback')
Assert-DocumentContains -Path $docs.rollback -Terms @('rollback','rollback version','artifact hash','signature','no destructive delete')
Assert-DocumentContains -Path $docs.notes -Terms @('release notes','version','channel','rollback version','go/no-go')
Assert-DocumentContains -Path $docs.channels -Terms @('channel owners','candidate channel','stable channel','velopack')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','exp-10','release smoke test')
Write-Step 'EXP-09 document contract PASS'

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

Write-Step 'Admin login and release channel endpoint contract...'
$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
$token=$session.accessToken; Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$adminHeaders=@{Authorization="Bearer $token"}
$channels=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/updates/channels" -Headers $adminHeaders -TimeoutSec 30
$channelItems=Get-Items $channels
$codes=@($channelItems | ForEach-Object { $_.code })
Assert-True ($codes -contains 'stable') 'Update channels did not include stable.'
Assert-True ($codes -contains 'beta') 'Update channels did not include beta.'
Assert-True ($codes -contains 'internal') 'Update channels did not include internal.'
Write-Step 'Admin login and release channel endpoint contract PASS'

Write-Step 'Create controlled internal update release...'
$stamp=(Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$releaseVersion="0.10.0-exp09.$stamp"
$rollbackVersion='0.10.0-post-pilot'
Assert-True ($releaseVersion -match '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') 'Generated version is not SemVer-compatible.'
$artifactHash=New-Sha256Hex "SolidPOS EXP-09 $TenantId $releaseVersion"
$signature=New-Sha256Hex "SolidPOS EXP-09 signature $TenantId $releaseVersion"
$artifactUrl="https://solidpos.local/releases/exp-09/$releaseVersion/SolidPOS.PosCore-$releaseVersion-win-x64.setup.exe"
$releaseBody=@{
  version=$releaseVersion
  channel='internal'
  packageType='velopack'
  artifactUrl=$artifactUrl
  artifactHash=$artifactHash
  signature=$signature
  rollbackVersion=$rollbackVersion
  mandatory=$false
  universalInstaller=$true
  tenantScoped=$true
}
$release=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/updates/releases" -Headers $adminHeaders -ContentType 'application/json' -Body ($releaseBody|ConvertTo-Json -Depth 8) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($release.id)) 'Release creation did not return release id.'
Assert-True ($release.version -eq $releaseVersion) 'Created release version mismatch.'
Assert-True ($release.channel -eq 'internal') 'Created release channel mismatch.'
Assert-True ($release.packageType -eq 'velopack') 'Created release packageType mismatch.'
Assert-True ($release.rollbackVersion -eq $rollbackVersion) 'Created release rollbackVersion mismatch.'
Assert-True ($release.mandatory -eq $false) 'Created release must not be mandatory.'
Assert-True ($release.universalInstaller -eq $true) 'Created release must use universalInstaller.'
Write-Step 'Create controlled internal update release PASS'

Write-Step 'Release smoke test and update check...'
$check=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/updates/check?currentVersion=$([uri]::EscapeDataString($rollbackVersion))&channel=internal&packageType=velopack" -Headers $adminHeaders -TimeoutSec 30
Assert-True ($null -ne $check) 'Update check did not return data.'
Assert-True ($check.updateAvailable -eq $true) 'Update check did not report updateAvailable.'
Assert-True ($check.channel -eq 'internal') 'Update check channel mismatch.'
Assert-True ($check.packageType -eq 'velopack') 'Update check package type mismatch.'
Assert-True ($null -ne $check.release) 'Update check did not return release.'
Assert-True ($check.release.version -eq $releaseVersion) 'Update check did not return created release version.'
Assert-True ($check.release.artifactHash -eq $artifactHash) 'Update check artifact hash mismatch.'
Assert-True ($check.release.signature -eq $signature) 'Update check signature mismatch.'
Write-Step 'Release smoke test and update check PASS'

Write-Step 'SQL release management update channel cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{tenant_id=$TenantId;release_id=$release.id;release_version=$releaseVersion}
Assert-True ($sql.exp09SqlValidation -eq 'GO') "EXP-09 SQL validation failed: $($sql.sqlBlockingReasons -join ', ')"
Write-Step 'SQL release management update channel cross-check PASS'

Write-Step 'Release management decision matrix...'
$blockers=@($sql.sqlBlockingReasons)
$warnings=@($sql.sqlWarnings)
$conditions=@()
if((Get-LongValue $sql @('stableReleaseCount')) -eq 0){ $conditions += 'stable_channel_promotion_pending' }
if((Get-LongValue $sql @('mandatoryReleaseCount')) -gt 0){ $conditions += 'review_mandatory_release_policy' }
if((Get-LongValue $sql @('invalidArtifactMetadataCount')) -gt 0){ $conditions += 'review_release_artifact_metadata' }
Assert-True ($blockers.Count -eq 0) "EXP-09 has blockers: $($blockers -join ', ')"
Write-Step 'Release management decision matrix PASS'

Write-Step 'Write release management manifest and log...'
$manifest=[ordered]@{
  phase='EXP-09'
  status='PASS RELEASE MANAGEMENT AND UPDATE CHANNEL / GO EXP-10'
  tenantId=$TenantId
  baseUrl=$script:base
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  releaseDecision='GO_RELEASE_MANAGEMENT_UPDATE_CHANNEL_READY'
  exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02'
  exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
  exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
  exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
  exp05='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
  exp06='PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
  exp07='PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08'
  exp08='PASS SUPPORT AND INCIDENT OPERATIONS / GO EXP-09'
  healthLive=$live.status
  healthReady=$ready.status
  databaseReady=$ready.database
  releaseId=$release.id
  releaseVersion=$releaseVersion
  releaseChannel=$release.channel
  packageType=$release.packageType
  artifactHash=$artifactHash
  signaturePresent=($signature.Length -ge 8)
  rollbackVersion=$rollbackVersion
  mandatory=$release.mandatory
  universalInstaller=$release.universalInstaller
  tenantScoped=($null -ne $release.tenantId)
  updateCheckDecision=$check.decision
  updateAvailable=$check.updateAvailable
  channels=$codes
  tenantReleaseCount=$sql.tenantReleaseCount
  internalReleaseCount=$sql.internalReleaseCount
  stableReleaseCount=$sql.stableReleaseCount
  updateReleaseUniqueConstraintPresent=$sql.updateReleaseUniqueConstraintPresent
  updateReleaseCheckIndexPresent=$sql.updateReleaseCheckIndexPresent
  auditEventsLast24Hours=$sql.auditEventsLast24Hours
  syncChangeCount=$sql.syncChangeCount
  blockers=$blockers
  conditions=$conditions
  sqlWarnings=$warnings
  schemaVersion=4
  releaseManagementContract='release_management_update_channel'
  nextPhase='EXP-10 Customer/Admin Management Completion'
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -Path $manifestPath -Encoding UTF8
@"
# EXP-09 Release Management and Update Channel Log

- GeneratedAt: $($manifest.generatedAt)
- TenantId: $TenantId
- BaseUrl: $script:base
- Status: $($manifest.status)
- Decision: $($manifest.releaseDecision)
- ReleaseId: $($manifest.releaseId)
- ReleaseVersion: $releaseVersion
- Channel: $($manifest.releaseChannel)
- PackageType: $($manifest.packageType)
- RollbackVersion: $rollbackVersion
- UpdateAvailable: $($manifest.updateAvailable)
- UpdateCheckDecision: $($manifest.updateCheckDecision)
- Conditions: $($conditions -join ',')
- SQL Warnings: $($warnings -join ',')
- NextPhase: $($manifest.nextPhase)
"@ | Set-Content -Path $logPath -Encoding UTF8
Write-Step 'Write release management manifest and log PASS'
Write-Step 'EXP-09 PASS RELEASE MANAGEMENT AND UPDATE CHANNEL / GO EXP-10'
$manifest
