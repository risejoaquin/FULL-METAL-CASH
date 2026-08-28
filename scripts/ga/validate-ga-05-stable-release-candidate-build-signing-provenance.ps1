param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [string]$ReleaseVersion='1.0.0-rc.1',
 [string]$SigningCertificateThumbprint='',
 [int]$ArtifactRetentionDays=180,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[GA-05] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try {[Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)} finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)} }
function Invoke-NativeChecked { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){throw "$Name failed with exit code $LASTEXITCODE."}; $global:LASTEXITCODE=0 }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
function Get-RelativePathCompat { param([string]$BasePath,[string]$TargetPath) $baseUri=New-Object System.Uri(([IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar)); $targetUri=New-Object System.Uri([IO.Path]::GetFullPath($TargetPath)); return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/',[IO.Path]::DirectorySeparatorChar) }
function Get-FileSha256 { param([string]$Path) return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant() }
function Get-TreeFingerprint { param([string]$Root) $lines=New-Object System.Collections.Generic.List[string]; Get-ChildItem -Path $Root -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | Sort-Object FullName | ForEach-Object { $rel=(Get-RelativePathCompat -BasePath $Root -TargetPath $_.FullName).Replace('\','/'); $lines.Add("$rel|$($_.Length)|$(Get-FileSha256 $_.FullName)") }; $payload=[string]::Join("`n",$lines); $sha=[Security.Cryptography.SHA256]::Create(); try { $bytes=[Text.Encoding]::UTF8.GetBytes($payload); return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant() } finally {$sha.Dispose()} }
function Get-RepositoryFingerprint { param([string]$Root) $skip='\\(\.git|\.runtime|bin|obj|node_modules|dist|TestResults)\\|\\docs\\.*\\logs\\'; $lines=New-Object System.Collections.Generic.List[string]; Get-ChildItem -Path $Root -Recurse -File | Where-Object { $_.FullName -notmatch $skip } | Sort-Object FullName | ForEach-Object { $rel=(Get-RelativePathCompat -BasePath $Root -TargetPath $_.FullName).Replace('\','/'); $lines.Add("$rel|$($_.Length)|$(Get-FileSha256 $_.FullName)") }; $payload=[string]::Join("`n",$lines); $sha=[Security.Cryptography.SHA256]::Create(); try { $bytes=[Text.Encoding]::UTF8.GetBytes($payload); return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant() } finally {$sha.Dispose()} }
function Get-AuthenticodeEvidence { param([string]$Path,[string]$ExpectedThumbprint) $sig=Get-AuthenticodeSignature -FilePath $Path; Assert-True ($null -ne $sig.SignerCertificate) "Authenticode signer missing for $Path"; Assert-True ($sig.SignerCertificate.Thumbprint -eq $ExpectedThumbprint) "Authenticode signer thumbprint mismatch for $Path"; Assert-True ($sig.Status -eq 'Valid') "Authenticode signature is not Valid for $Path. Status=$($sig.Status); Message=$($sig.StatusMessage)"; return [ordered]@{path=$Path;status=[string]$sig.Status;subject=$sig.SignerCertificate.Subject;thumbprint=$sig.SignerCertificate.Thumbprint;notAfter=$sig.SignerCertificate.NotAfter.ToUniversalTime().ToString('o')} }

$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=(Resolve-Path (Join-Path $scriptRoot '..\..')).Path
$sln=Join-Path $repoRoot 'solidpos-platform.sln'
$posCoreProject=Join-Path $repoRoot 'src\PosCore\SolidPOS.PosCore.Wpf\SolidPOS.PosCore.Wpf.csproj'
$appStartup=Join-Path $repoRoot 'src\PosCore\SolidPOS.PosCore.Wpf\App.xaml.cs'
$programEntry=Join-Path $repoRoot 'src\PosCore\SolidPOS.PosCore.Wpf\Program.cs'
$ga04=Join-Path $scriptRoot 'validate-ga-04-production-data-integrity-financial-reconciliation.ps1'
$ga04Manifest=Join-Path $repoRoot '.runtime\ga-04-production-data-integrity-financial-reconciliation\ga-04-manifest.json'
$secretScan=Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1'
$checkSql=Join-Path $scriptRoot 'ga-05-stable-release-candidate-build-signing-provenance-check.sql'
$runtime=Join-Path $repoRoot '.runtime\ga-05-stable-release-candidate-build-signing-provenance'
$publishA=Join-Path $runtime 'publish-a'
$publishB=Join-Path $runtime 'publish-b'
$releaseDir=Join-Path $runtime 'velopack-release'
$toolsDir=Join-Path $runtime 'tools'
$artifactDir=Join-Path $runtime 'artifacts'
$manifestPath=Join-Path $runtime 'ga-05-manifest.json'
$evidencePath=Join-Path $runtime 'ga-05-evidence.md'
$snapshotPath=Join-Path $runtime 'ga-05-snapshot.json'
$metadataPath=Join-Path $runtime 'ga-05-release-candidate-metadata.json'
$provenancePath=Join-Path $runtime 'ga-05-provenance.json'
$dependencyInventoryPath=Join-Path $runtime 'ga-05-dotnet-dependency-inventory.json'
$retentionPath=Join-Path $runtime 'ga-05-artifact-retention.json'
$releaseNotesPath=Join-Path $runtime 'ga-05-release-notes.md'
$logPath=Join-Path $repoRoot 'docs\ga\logs\ga-05-stable-release-candidate-build-signing-provenance-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_GENERAL_AVAILABILITY_READINESS_ROADMAP_20260821.md'),
 (Join-Path $repoRoot 'SOLIDPOS_GA_05_STABLE_RELEASE_CANDIDATE_BUILD_SIGNING_AND_PROVENANCE.md'),
 (Join-Path $repoRoot 'docs\ga\ga-05-stable-release-candidate-build-signing-provenance.md'),
 (Join-Path $repoRoot 'docs\ga\ga-05-signing-policy.md'),
 (Join-Path $repoRoot 'docs\ga\ga-05-provenance-and-retention.md'),
 (Join-Path $repoRoot 'docs\ga\ga-05-release-candidate-checklist.md'),
 (Join-Path $repoRoot 'docs\ga\ga-05-go-no-go.md')
)

Assert-True ($ReleaseVersion -match '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') 'ReleaseVersion must be SemVer-compatible.'
Assert-True ($ArtifactRetentionDays -ge 30) 'ArtifactRetentionDays must be at least 30 days.'
Remove-Item -Recurse -Force $runtime -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $runtime,$publishA,$publishB,$releaseDir,$toolsDir,$artifactDir,(Split-Path -Parent $logPath) | Out-Null

Write-Step 'Repository/document GA-05 guardrails...'
Assert-True (Test-Path $sln) 'solidpos-platform.sln missing.'
Assert-True (Test-Path $posCoreProject) 'PosCore WPF project missing.'
Assert-True (Test-Path $appStartup) 'PosCore WPF App.xaml.cs missing.'
Assert-True (Test-Path $programEntry) 'PosCore WPF Program.cs entry point missing.'
Assert-True (Test-Path $ga04) 'GA-04 validator missing.'
Assert-True (Test-Path $secretScan) 'Secret scan missing.'
Assert-True (Test-Path $checkSql) 'GA-05 SQL check missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required GA-05 document missing: $d"}
Assert-DocumentContains $docs[0] @('GA-05','Stable Release Candidate Build, Signing and Provenance','packageType = velopack','signaturePresent = True','universalInstaller = True','rollbackVersion != null','mandatory = False','tenantScoped = True','schemaVersion = 4')
$mainState=(Get-Content -Raw $docs[1]).ToLowerInvariant(); Assert-True ($mainState.Contains('pending user validation') -or $mainState.Contains('pass real production')) 'GA-05 main lifecycle state missing.'
Assert-DocumentContains $docs[1] @('Velopack 1.2.0','build reproducibility','Authenticode','provenance','artifact retention','GA-06','generalAvailabilityActivated = False')
Assert-DocumentContains $docs[2] @('release candidate','stable','not promoted','rollback','schema version 4','update contract')
Assert-DocumentContains $docs[3] @('production certificate','validation_self_signed','authenticode','thumbprint','GA-06')
Assert-DocumentContains $docs[4] @('source reference','sha-256','dependency inventory','retention','180')
Assert-DocumentContains $docs[5] @('dotnet restore','dotnet build','dotnet test','dotnet publish','vpk pack','signature verification')
$goState=(Get-Content -Raw $docs[6]).ToLowerInvariant(); Assert-True ($goState.Contains('pending user validation') -or $goState.Contains('pass real production')) 'GA-05 go/no-go lifecycle state missing.'
Assert-DocumentContains $docs[6] @('PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06','FAIL / HOTFIX REQUIRED')
$projectText=Get-Content -Raw $posCoreProject
$appText=Get-Content -Raw $appStartup
$programText=Get-Content -Raw $programEntry
Assert-True ($projectText.Contains('PackageReference Include="Velopack" Version="1.2.0"')) 'PosCore WPF must pin Velopack 1.2.0.'
Assert-True ($projectText.Contains('<StartupObject>SolidPOS.PosCore.Wpf.Program</StartupObject>')) 'PosCore WPF must use Program.Main as the explicit startup object.'
Assert-True ($programText.Contains('public static void Main()')) 'PosCore WPF Program.Main entry point missing.'
Assert-True ($programText.Contains('VelopackApp.Build().Run()')) 'PosCore WPF Program.Main must initialize Velopack lifecycle hooks.'
Assert-True (-not $appText.Contains('VelopackApp.Build().Run()')) 'Velopack lifecycle hook must not remain in the App constructor.'
Write-Step 'Repository/document GA-05 guardrails PASS'

Write-Step 'Secret scan...'
Unblock-File $secretScan -ErrorAction SilentlyContinue
& $secretScan -Root $repoRoot
Write-Step 'Secret scan PASS'

Write-Step 'Fresh GA-04 prerequisite revalidation...'
Unblock-File $ga04 -ErrorAction SilentlyContinue
& $ga04 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
Assert-True (Test-Path $ga04Manifest) 'Fresh GA-04 manifest missing.'
$g4=Get-Content -Raw $ga04Manifest | ConvertFrom-Json
Assert-True ($g4.status -eq 'PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05') 'Fresh GA-04 prerequisite did not PASS.'
Assert-True (@($g4.blockers).Count -eq 0) 'Fresh GA-04 prerequisite contains blockers.'
Assert-True ([int]$g4.schemaVersion -eq 4) 'Fresh GA-04 schemaVersion drifted from 4.'
Assert-True ([string]$g4.syncContract -eq 'schema_version_4') 'Fresh GA-04 syncContract drifted.'
Assert-True (-not [bool]$g4.generalAvailabilityActivated) 'General Availability is already activated; GA-05 must stop.'
$ga04At=[string]$g4.generatedAt
Write-Step 'Fresh GA-04 prerequisite revalidation PASS'

Write-Step 'Production release/update baseline gate...'
$live=Invoke-RestMethod -Method Get -Uri "$base/health/live" -TimeoutSec 30
$ready=Invoke-RestMethod -Method Get -Uri "$base/health/ready" -TimeoutSec 30
Assert-True ($live.status -eq 'alive') 'Production liveness is not alive.'
Assert-True ($ready.status -eq 'ready' -and $ready.database -eq 'ready') 'Production readiness/database is not ready.'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Login did not return accessToken.'
$headers=@{Authorization="Bearer $($session.accessToken)"}
$channels=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/channels" -Headers $headers -TimeoutSec 30
$channelItems=@(); if($channels -is [System.Array]){$channelItems=@($channels)} elseif($null -ne $channels.items){$channelItems=@($channels.items)} else {$channelItems=@($channels)}
$stable=@($channelItems | Where-Object {$_.code -eq 'stable'})
Assert-True ($stable.Count -eq 1) 'Stable update channel contract missing or duplicated.'
$sql=Invoke-DbJsonFile $checkSql @{tenant_id=$TenantId;ga04_at=$ga04At}
Assert-True ([string]$sql.ga05SqlContract -eq 'ga_stable_release_candidate_build_signing_provenance') 'GA-05 SQL contract mismatch.'
Assert-True ([string]$sql.ga05SqlDecision -eq 'GO') "GA-05 SQL blockers: $(@($sql.blockers) -join ', ')"
Assert-True ([long]$sql.activeStableReleaseCount -eq 0) 'Stable channel is already promoted; GA-05 must not proceed.'
Assert-True (-not [string]::IsNullOrWhiteSpace([string]$sql.rollbackCandidateVersion)) 'Rollback candidate version missing.'
$rollbackVersion=[string]$sql.rollbackCandidateVersion
Write-Step ("Production release/update baseline gate PASS; rollbackVersion={0}; activeStableReleaseCount={1}" -f $rollbackVersion,$sql.activeStableReleaseCount)

Write-Step 'Release restore/build/test...'
Invoke-NativeChecked 'dotnet restore' { & dotnet restore $sln }
Invoke-NativeChecked 'dotnet build Release' { & dotnet build $sln -c Release --no-restore }
Invoke-NativeChecked 'dotnet test Release' { & dotnet test $sln -c Release --no-build }
Write-Step 'Release restore/build/test PASS'

if(-not $SkipDashboardBuild){
  $dashboard=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
  if(Test-Path (Join-Path $dashboard 'package.json')){
    Write-Step 'PosDashboard production build...'
    Push-Location $dashboard
    try { if(Test-Path 'package-lock.json'){Invoke-NativeChecked 'npm ci' {& npm ci}} else {Invoke-NativeChecked 'npm install' {& npm install}}; Invoke-NativeChecked 'npm run build' {& npm run build} } finally {Pop-Location}
    Write-Step 'PosDashboard production build PASS'
  }
}

Write-Step 'Deterministic publish reproducibility gate...'
# GA-05.1: publish is RID-specific. A solution-level restore without a RID can produce
# project.assets.json for net8.0-windows only, which makes --no-restore publish -r win-x64
# fail with NETSDK1047. Restore the exact WPF publish graph for win-x64 first and keep
# both publish passes on the identical restored graph.
Invoke-NativeChecked 'dotnet restore PosCore WPF win-x64' { & dotnet restore $posCoreProject -r win-x64 -p:RuntimeIdentifiers=win-x64 }
Remove-Item -Recurse -Force $publishA,$publishB -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishA,$publishB | Out-Null
$commonPublish=@($posCoreProject,'-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:RuntimeIdentifiers=win-x64','-p:ContinuousIntegrationBuild=true','-p:Deterministic=true',"-p:Version=$ReleaseVersion")
Invoke-NativeChecked 'dotnet publish A' { & dotnet publish @commonPublish -o $publishA }
Invoke-NativeChecked 'dotnet publish B' { & dotnet publish @commonPublish -o $publishB }
$publishHashA=Get-TreeFingerprint $publishA
$publishHashB=Get-TreeFingerprint $publishB
Assert-True ($publishHashA -eq $publishHashB) "Release publish is not reproducible. publishA=$publishHashA publishB=$publishHashB"
$mainExe=Join-Path $publishA 'SolidPOS.PosCore.Wpf.exe'
Assert-True (Test-Path $mainExe) 'Published main executable missing.'
Write-Step ("Deterministic publish reproducibility gate PASS; publishPayloadSha256={0}" -f $publishHashA)

Write-Step 'Pinned Velopack CLI acquisition...'
$vpk=Join-Path $toolsDir 'vpk.exe'
if(-not (Test-Path $vpk)){
  Invoke-NativeChecked 'dotnet tool install vpk 1.2.0' { & dotnet tool install --tool-path $toolsDir vpk --version 1.2.0 }
}
Assert-True (Test-Path $vpk) 'Pinned Velopack vpk.exe was not installed.'
$toolListOutput = (& dotnet tool list --tool-path $toolsDir 2>&1 | Out-String)
if($LASTEXITCODE -ne 0){ throw "dotnet tool list failed with exit code $LASTEXITCODE. Output: $toolListOutput" }
$toolListLine = ($toolListOutput -split '[\r\n]+' | Where-Object { $_ -match '^\s*vpk\s+' } | Select-Object -First 1)
Assert-True (-not [string]::IsNullOrWhiteSpace($toolListLine)) "Pinned Velopack vpk package was not found in dotnet tool list. Output: $toolListOutput"
Assert-True ($toolListLine -match '\b1\.2\.0\b') "Velopack CLI version mismatch. Expected 1.2.0. Tool list: $toolListLine"
$vpkVersion='1.2.0'
Write-Step ("Pinned Velopack CLI acquisition PASS; version={0}" -f $vpkVersion)

$validationCertCreated=$false
$trustedPeopleAdded=$false
$trustedRootAdded=$false
$signingIdentityType='PROVIDED_CODE_SIGNING_CERTIFICATE'
$cert=$null
try {
  Write-Step 'Code-signing identity preparation...'
  if(-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)){
  $thumb=$SigningCertificateThumbprint.Replace(' ','').ToUpperInvariant()
  $cert=Get-ChildItem -Path "Cert:\CurrentUser\My\$thumb" -ErrorAction SilentlyContinue
  Assert-True ($null -ne $cert) "Signing certificate not found in CurrentUser\\My: $thumb"
  Assert-True ($cert.HasPrivateKey) 'Signing certificate does not have a private key.'
} else {
  $signingIdentityType='VALIDATION_SELF_SIGNED'
  $cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SolidPOS GA-05 Validation $ReleaseVersion" -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(7)
  $validationCertCreated=$true
  $publicCert=Join-Path $runtime 'ga-05-validation-signing.cer'
  Export-Certificate -Cert $cert -FilePath $publicCert -Force | Out-Null
  Import-Certificate -FilePath $publicCert -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
  $trustedRootAdded=$true
  Import-Certificate -FilePath $publicCert -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
  $trustedPeopleAdded=$true
  Assert-True ($null -ne (Get-ChildItem -Path "Cert:\CurrentUser\Root\$($cert.Thumbprint)" -ErrorAction SilentlyContinue)) 'Temporary validation signing certificate was not installed in CurrentUser\Root.'
  Assert-True ($null -ne (Get-ChildItem -Path "Cert:\CurrentUser\TrustedPeople\$($cert.Thumbprint)" -ErrorAction SilentlyContinue)) 'Temporary validation signing certificate was not installed in CurrentUser\TrustedPeople.'
}
  $signingThumbprint=$cert.Thumbprint
  Assert-True (-not [string]::IsNullOrWhiteSpace($signingThumbprint)) 'Signing certificate thumbprint missing.'
  Write-Step ("Code-signing identity preparation PASS; type={0}; thumbprint={1}" -f $signingIdentityType,$signingThumbprint)

  Write-Step 'Sign published PosCore executable...'
  $signedMain=Set-AuthenticodeSignature -FilePath $mainExe -Certificate $cert -HashAlgorithm SHA256
  Assert-True ($null -ne $signedMain.SignerCertificate) 'Set-AuthenticodeSignature did not sign PosCore executable.'
  $mainSignature=Get-AuthenticodeEvidence $mainExe $signingThumbprint
  Write-Step 'Sign published PosCore executable PASS'

  @"
# SolidPOS GA-05 Stable Release Candidate $ReleaseVersion

- Channel target: stable (candidate only; not promoted)
- Package: Velopack universal installer
- Rollback version: $rollbackVersion
- Mandatory: false
- Tenant scoped: true
- Schema contract: schema_version_4
- General Availability activation: false
- Signing identity type: $signingIdentityType
"@ | Set-Content -Encoding UTF8 $releaseNotesPath

  Write-Step 'Build real Velopack universal installer...'
  Remove-Item -Recurse -Force $releaseDir -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
  Invoke-NativeChecked 'vpk pack' { & $vpk pack --packId 'SolidPOS.PosCore' --packVersion $ReleaseVersion --packDir $publishA --mainExe 'SolidPOS.PosCore.Wpf.exe' --outputDir $releaseDir --channel 'stable' --releaseNotes $releaseNotesPath }
  $setups=@(Get-ChildItem -Path $releaseDir -File | Where-Object {$_.Name -match 'Setup\.exe$'})
  Assert-True ($setups.Count -eq 1) "Expected exactly one Velopack Setup.exe, found $($setups.Count)."
  $setup=$setups[0]
  $nupkgs=@(Get-ChildItem -Path $releaseDir -File -Filter '*.nupkg')
  Assert-True ($nupkgs.Count -ge 1) 'Velopack release package (.nupkg) missing.'
  $releaseIndexPath=Join-Path $releaseDir 'releases.stable.json'
  Assert-True (Test-Path $releaseIndexPath) 'Velopack releases.stable.json missing for explicit stable channel.'
  $releaseIndexRaw=Get-Content -Raw $releaseIndexPath
  Assert-True (-not [string]::IsNullOrWhiteSpace($releaseIndexRaw)) 'Velopack releases.stable.json is empty.'
  try { $releaseIndex=$releaseIndexRaw | ConvertFrom-Json } catch { throw "Velopack releases.stable.json is not valid JSON: $($_.Exception.Message)" }
  $releaseAssets=@($releaseIndex.Assets)
  Assert-True ($releaseAssets.Count -ge 1) 'Velopack releases.stable.json contains no release assets.'
  $rcAssets=@($releaseAssets | Where-Object { [string]$_.Version -eq $ReleaseVersion })
  Assert-True ($rcAssets.Count -ge 1) "Velopack releases.stable.json does not reference RC version $ReleaseVersion."
  $fullRcAssets=@($rcAssets | Where-Object { [string]$_.Type -eq 'Full' -or [string]$_.Type -eq 'full' })
  Assert-True ($fullRcAssets.Count -eq 1) "Expected exactly one Full asset for RC $ReleaseVersion in releases.stable.json, found $($fullRcAssets.Count)."
  $fullRcFileName=[string]$fullRcAssets[0].FileName
  Assert-True (-not [string]::IsNullOrWhiteSpace($fullRcFileName)) 'Full RC asset in releases.stable.json has no FileName.'
  Assert-True (Test-Path (Join-Path $releaseDir $fullRcFileName)) "Full RC asset referenced by releases.stable.json is missing: $fullRcFileName"
  Assert-True (@($nupkgs | Where-Object { $_.Name -eq $fullRcFileName }).Count -eq 1) "Full RC asset referenced by releases.stable.json is not one of the generated .nupkg files: $fullRcFileName"
  Write-Step ("Build real Velopack universal installer PASS; setup={0}; releaseIndex={1}; fullPackage={2}" -f $setup.Name,(Split-Path -Leaf $releaseIndexPath),$fullRcFileName)

  Write-Step 'Authenticode sign and verify Velopack installer...'
  $signedSetup=Set-AuthenticodeSignature -FilePath $setup.FullName -Certificate $cert -HashAlgorithm SHA256
  Assert-True ($null -ne $signedSetup.SignerCertificate) 'Set-AuthenticodeSignature did not sign Velopack installer.'
  $setupSignature=Get-AuthenticodeEvidence $setup.FullName $signingThumbprint
  $artifactHash=Get-FileSha256 $setup.FullName
  Assert-True ($artifactHash -match '^[0-9a-f]{64}$') 'Installer SHA-256 invalid.'
  Write-Step ("Authenticode sign and verify Velopack installer PASS; artifactSha256={0}" -f $artifactHash)

  Write-Step 'Dependency inventory, provenance and artifact retention evidence...'
  $dependencyRaw=& dotnet list $sln package --include-transitive --format json 2>&1
  if($LASTEXITCODE -ne 0){throw "dotnet list package dependency inventory failed with exit code $LASTEXITCODE."}
  $global:LASTEXITCODE=0
  $dependencyRaw | Set-Content -Encoding UTF8 $dependencyInventoryPath
  Assert-True ((Get-Item $dependencyInventoryPath).Length -gt 100) 'Dependency inventory is unexpectedly empty.'
  $sourceReference=Get-RepositoryFingerprint $repoRoot
  $gitCommit=$null
  $git=(Get-Command git -ErrorAction SilentlyContinue)
  if($null -ne $git -and (Test-Path (Join-Path $repoRoot '.git'))){ $gitCommit=(& git -C $repoRoot rev-parse HEAD 2>$null | Select-Object -First 1) }
  $artifactRecords=@()
  Get-ChildItem -Path $releaseDir -File | Sort-Object Name | ForEach-Object { $artifactRecords += [ordered]@{name=$_.Name;sizeBytes=$_.Length;sha256=Get-FileSha256 $_.FullName} }
  $generated=(Get-Date).ToUniversalTime()
  $retainUntil=$generated.AddDays($ArtifactRetentionDays)
  $provenance=[ordered]@{
    contract='ga_stable_release_candidate_build_signing_provenance';generatedAt=$generated.ToString('o');tenantId=$TenantId;releaseVersion=$ReleaseVersion;targetChannel='stable';promotionPerformed=$false;
    source=[ordered]@{sourceReferenceType='repository_sha256';repositorySha256=$sourceReference;gitCommit=$gitCommit;sourceGa04ZipSha256='33313dfc7c85cc855286e26e5d263eeece171ead9c6dbbfb300bc4d6663f37e6'};
    build=[ordered]@{configuration='Release';runtime='win-x64';selfContained=$true;deterministic=$true;publishPayloadSha256=$publishHashA;secondPublishPayloadSha256=$publishHashB;velopackVersion=$vpkVersion;velopackPackageVersion='1.2.0'};
    signing=[ordered]@{identityType=$signingIdentityType;certificateSubject=$cert.Subject;certificateThumbprint=$signingThumbprint;mainExecutable=$mainSignature;installer=$setupSignature;productionSigningRequiredBeforePublicPromotion=($signingIdentityType -eq 'VALIDATION_SELF_SIGNED')};
    artifact=[ordered]@{packageType='velopack';universalInstaller=$true;installerFile=$setup.Name;artifactHash=$artifactHash;signaturePresent=$true;signatureValid=$true;mandatory=$false;tenantScoped=$true;rollbackVersion=$rollbackVersion;releaseFiles=$artifactRecords};
    compatibility=[ordered]@{schemaVersion=4;syncContract='schema_version_4';updateContract='builder_updates_v1';generalAvailabilityActivated=$false};
    dependencyInventory=(Split-Path -Leaf $dependencyInventoryPath);retention=[ordered]@{days=$ArtifactRetentionDays;retainUntilUtc=$retainUntil.ToString('o');automaticDeletion=$false}
  }
  $provenance | ConvertTo-Json -Depth 16 | Set-Content -Encoding UTF8 $provenancePath
  $metadata=[ordered]@{releaseVersion=$ReleaseVersion;channel='stable';candidateOnly=$true;packageType='velopack';signaturePresent=$true;signatureValid=$true;signingIdentityType=$signingIdentityType;universalInstaller=$true;rollbackVersion=$rollbackVersion;mandatory=$false;tenantScoped=$true;schemaVersion=4;syncContract='schema_version_4';sourceReference="repo-sha256:$sourceReference";artifactHash=$artifactHash;artifactFile=$setup.Name;provenanceFile=(Split-Path -Leaf $provenancePath);generalAvailabilityActivated=$false}
  $metadata | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $metadataPath
  [ordered]@{generatedAt=$generated.ToString('o');retentionDays=$ArtifactRetentionDays;retainUntilUtc=$retainUntil.ToString('o');automaticDeletion=$false;artifactCount=$artifactRecords.Count;artifacts=$artifactRecords} | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $retentionPath
  Copy-Item -Force $setup.FullName (Join-Path $artifactDir $setup.Name)
  Copy-Item -Force $releaseIndexPath (Join-Path $artifactDir (Split-Path -Leaf $releaseIndexPath))
  $nupkgs | ForEach-Object { Copy-Item -Force $_.FullName (Join-Path $artifactDir $_.Name) }
  Copy-Item -Force $metadataPath,$provenancePath,$dependencyInventoryPath,$releaseNotesPath,$retentionPath -Destination $artifactDir
  Write-Step 'Dependency inventory, provenance and artifact retention evidence PASS'

  Write-Step 'GA-05 final blocker matrix...'
  $blockers=@($sql.blockers)
  if($publishHashA -ne $publishHashB){$blockers+='release_publish_not_reproducible'}
  if($setupSignature.status -ne 'Valid'){$blockers+='installer_authenticode_invalid'}
  if($mainSignature.status -ne 'Valid'){$blockers+='main_executable_authenticode_invalid'}
  if([string]::IsNullOrWhiteSpace($rollbackVersion)){$blockers+='rollback_version_missing'}
  if([long]$sql.activeStableReleaseCount -ne 0){$blockers+='stable_channel_already_promoted'}
  Assert-True ($blockers.Count -eq 0) "GA-05 blockers: $($blockers -join ', ')"
  Write-Step 'GA-05 final blocker matrix PASS'

  $snapshot=[ordered]@{phase='GA-05';generatedAt=$generated.ToString('o');tenantId=$TenantId;baseUrl=$base;ga04At=$ga04At;sql=$sql;metadata=$metadata;provenance=$provenance;blockers=$blockers}
  $snapshot | ConvertTo-Json -Depth 18 | Set-Content -Encoding UTF8 $snapshotPath
  @"
# GA-05 Stable Release Candidate Build, Signing and Provenance Evidence

Generated: $($generated.ToString('o'))
Tenant: $TenantId
Release version: $ReleaseVersion
Rollback version: $rollbackVersion

- packageType: velopack
- universalInstaller: true
- signaturePresent: true
- signatureValid: true
- signingIdentityType: $signingIdentityType
- artifactSha256: $artifactHash
- publishPayloadSha256: $publishHashA
- reproduciblePublish: true
- mandatory: false
- tenantScoped: true
- schemaVersion: 4
- syncContract: schema_version_4
- stablePromotionPerformed: false
- activeStableReleaseCount: $($sql.activeStableReleaseCount)
- artifactRetentionDays: $ArtifactRetentionDays
- productionSigningRequiredBeforePublicPromotion: $($signingIdentityType -eq 'VALIDATION_SELF_SIGNED')

PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06
General Availability remains NOT activated.
"@ | Set-Content -Encoding UTF8 $evidencePath
  $manifest=[ordered]@{
    phase='GA-05';status='PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated.ToString('o');entryGate='PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05';ga04At=$ga04At;
    releaseCandidateVersion=$ReleaseVersion;targetChannel='stable';candidateOnly=$true;stablePromotionPerformed=$false;packageType='velopack';velopackVersion=$vpkVersion;velopackPackageVersion='1.2.0';artifactFile=$setup.Name;artifactHash=$artifactHash;signaturePresent=$true;signatureValid=$true;signingIdentityType=$signingIdentityType;signerThumbprint=$signingThumbprint;universalInstaller=$true;rollbackVersion=$rollbackVersion;mandatory=$false;tenantScoped=$true;reproducibleBuild=$true;publishPayloadSha256=$publishHashA;sourceReference="repo-sha256:$sourceReference";gitCommit=$gitCommit;dependencyInventory='PASS';provenance='PASS';artifactRetentionDays=$ArtifactRetentionDays;retainUntilUtc=$retainUntil.ToString('o');productionSigningRequiredBeforePublicPromotion=($signingIdentityType -eq 'VALIDATION_SELF_SIGNED');activeStableReleaseCount=[long]$sql.activeStableReleaseCount;activeBetaReleaseCount=[long]$sql.activeBetaReleaseCount;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-06 - Stable Channel Promotion and Cohort Update Dry Run'
  }
  $manifest | ConvertTo-Json -Depth 14 | Set-Content -Encoding UTF8 $manifestPath
  @"
# GA-05 Stable Release Candidate Build, Signing and Provenance Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- releaseCandidateVersion: $ReleaseVersion
- rollbackVersion: $rollbackVersion
- packageType: velopack
- velopackVersion: $vpkVersion
- artifactFile: $($setup.Name)
- artifactHash: $artifactHash
- signaturePresent: true
- signatureValid: true
- signingIdentityType: $signingIdentityType
- universalInstaller: true
- mandatory: false
- tenantScoped: true
- reproducibleBuild: true
- sourceReference: repo-sha256:$sourceReference
- stablePromotionPerformed: false
- activeStableReleaseCount: $($sql.activeStableReleaseCount)
- blockers: {}
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False
"@ | Set-Content -Encoding UTF8 $logPath
  Write-Step 'GA-05 evidence manifest and stable RC snapshot PASS'
  Write-Step 'GA-05 PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06'
  [pscustomobject]$manifest
}
finally {
  if($validationCertCreated -and $null -ne $cert){
    $cleanupThumbprint=$cert.Thumbprint
    foreach($storeName in @('TrustedPeople','Root','My')){
      $storePath="Cert:\CurrentUser\$storeName\$cleanupThumbprint"
      if(Test-Path $storePath){
        $global:LASTEXITCODE=0
        & certutil.exe -user -delstore $storeName $cleanupThumbprint *> $null
        $cleanupExit=$LASTEXITCODE
        $global:LASTEXITCODE=0
        if($cleanupExit -ne 0 -and (Test-Path $storePath)){
          throw "Temporary GA-05 validation certificate cleanup failed for CurrentUser\$storeName. certutil exit code=$cleanupExit; thumbprint=$cleanupThumbprint"
        }
      }
    }
    $residualStores=@()
    foreach($storeName in @('TrustedPeople','Root','My')){
      if(Test-Path "Cert:\CurrentUser\$storeName\$cleanupThumbprint"){ $residualStores += $storeName }
    }
    if($residualStores.Count -gt 0){
      throw "Temporary GA-05 validation certificate remains installed after cleanup. Stores=$($residualStores -join ','); thumbprint=$cleanupThumbprint"
    }
  }
}
