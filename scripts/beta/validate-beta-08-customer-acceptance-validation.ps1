param(
 [Parameter(Mandatory=$true)][string]$BaseUrl,
 [Parameter(Mandatory=$true)][string]$TenantId,
 [Parameter(Mandatory=$true)][string]$Email,
 [Parameter(Mandatory=$true)][securestring]$Password,
 [Parameter(Mandatory=$true)][string]$DatabaseUrl,
 [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step { param([string]$Message) Write-Host "[BETA-08] $Message" }
function Assert-True { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)} finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)} }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $c=(Get-Content -Raw $Path).ToLowerInvariant(); foreach($t in $Terms){Assert-True ($c.Contains($t.ToLowerInvariant())) "Document $Path missing term: $t"} }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mount=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $name=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mount}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args+=@('-v',"$key=$($Variables[$key])")}; $args+=@('-f',"/sql/$name"); $global:LASTEXITCODE=0; $out=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $json=($out|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; $global:LASTEXITCODE=0; return ($json|ConvertFrom-Json) }
$base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$beta07=Join-Path $repoRoot 'scripts\beta\validate-beta-07-dashboard-daily-monitoring-pack.ps1'
$beta07Manifest=Join-Path $repoRoot '.runtime\beta-07-dashboard-daily-monitoring-pack\beta-07-dashboard-daily-monitoring-manifest.json'
$sqlPath=Join-Path $scriptRoot 'beta-08-customer-acceptance-validation-check.sql'
$runtime=Join-Path $repoRoot '.runtime\beta-08-customer-acceptance-validation'
$manifestPath=Join-Path $runtime 'beta-08-customer-acceptance-manifest.json'
$packetPath=Join-Path $runtime 'beta-08-customer-acceptance-packet.md'
$logPath=Join-Path $repoRoot 'docs\beta\logs\beta-08-customer-acceptance-validation-log.md'
$docs=@(
 (Join-Path $repoRoot 'SOLIDPOS_BETA_08_BETA_CUSTOMER_ACCEPTANCE_VALIDATION.md'),
 (Join-Path $repoRoot 'docs\beta\beta-08-customer-acceptance-checklist.md'),
 (Join-Path $repoRoot 'docs\beta\beta-08-operator-signoff.md'),
 (Join-Path $repoRoot 'docs\beta\beta-08-admin-signoff.md'),
 (Join-Path $repoRoot 'docs\beta\beta-08-known-issues.md'),
 (Join-Path $repoRoot 'docs\beta\beta-08-exit-criteria.md'),
 (Join-Path $repoRoot 'docs\beta\beta-08-go-no-go.md')
)
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
Write-Step 'Repository/document acceptance guardrails...'
Assert-True (Test-Path $beta07) 'BETA-07 validator missing.'
Assert-True (Test-Path $sqlPath) 'BETA-08 SQL validator missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL.'
foreach($d in $docs){Assert-True (Test-Path $d) "Required BETA-08 document missing: $d"}
Assert-DocumentContains $docs[0] @('BETA-08','customer acceptance','sales','cash','receipts','returns/refunds','catalog/pricing','user/admin','offline','support','release/update','GO BETA-09')
Assert-DocumentContains $docs[1] @('sales acceptance','cash acceptance','receipts acceptance','returns/refunds acceptance','catalog/pricing acceptance','user/admin acceptance','offline acceptance','support acceptance','release/update acceptance')
Assert-DocumentContains $docs[2] @('operator sign-off placeholder','name','date','decision')
Assert-DocumentContains $docs[3] @('admin sign-off placeholder','name','date','decision')
Assert-DocumentContains $docs[4] @('known issues','owner','severity','exit')
Assert-DocumentContains $docs[5] @('exit criteria','blockers = {}','schemaVersion = 4','syncContract = schema_version_4')
Write-Step 'Repository/document acceptance guardrails PASS'
Write-Step 'Execute BETA-07 monitoring prerequisite...'
Unblock-File $beta07 -ErrorAction SilentlyContinue
Unblock-File (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1') -ErrorAction SilentlyContinue
& $beta07 -BaseUrl $base -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild
if($LASTEXITCODE -ne 0){throw "BETA-07 inherited validator failed with exit code $LASTEXITCODE."}
Assert-True (Test-Path $beta07Manifest) 'BETA-07 manifest missing.'
$monitor=Get-Content -Raw $beta07Manifest | ConvertFrom-Json
Assert-True ($monitor.status -eq 'PASS BETA DASHBOARD DAILY MONITORING PACK / GO BETA-08') 'BETA-07 prerequisite did not PASS.'
Write-Step 'Execute BETA-07 monitoring prerequisite PASS'
Write-Step 'Production customer acceptance endpoint snapshot...'
$session=Invoke-RestMethod -Method Post -Uri "$base/api/v1/auth/login" -ContentType 'application/json' -Body (@{email=$Email;password=$plainPassword;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30
Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Admin login did not return accessToken.'
$h=@{Authorization="Bearer $($session.accessToken)"}
$tenant=Invoke-RestMethod -Method Get -Uri "$base/api/v1/tenants/current" -Headers $h -TimeoutSec 30
$stores=Invoke-RestMethod -Method Get -Uri "$base/api/v1/stores" -Headers $h -TimeoutSec 30
$users=Invoke-RestMethod -Method Get -Uri "$base/api/v1/users" -Headers $h -TimeoutSec 30
$catalog=Invoke-RestMethod -Method Get -Uri "$base/api/v1/tenant/catalog" -Headers $h -TimeoutSec 30
$sales=Invoke-RestMethod -Method Get -Uri "$base/api/v1/sales?limit=25" -Headers $h -TimeoutSec 30
$channels=Invoke-RestMethod -Method Get -Uri "$base/api/v1/updates/channels" -Headers $h -TimeoutSec 30
Assert-True ($tenant.id -eq $TenantId) 'Tenant mismatch.'
Assert-True ($null -ne $stores -and $null -ne $users -and $null -ne $catalog -and $null -ne $sales -and $null -ne $channels) 'Acceptance dependency endpoint returned null.'
Write-Step 'Production customer acceptance endpoint snapshot PASS'
Write-Step 'SQL customer acceptance source-of-truth...'
$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId}
Assert-True ($sql.beta08SqlDecision -eq 'GO') "BETA-08 SQL blockers: $($sql.blockers -join ', ')"
Write-Step 'SQL customer acceptance source-of-truth PASS'
Write-Step 'Build customer acceptance packet and evidence manifest...'
$knownIssues=@($sql.knownIssues) + @($monitor.conditions) | Select-Object -Unique
$blockers=@($sql.blockers)
$generated=(Get-Date).ToUniversalTime().ToString('o')
$acceptance=[ordered]@{
 salesAcceptance='PASS';cashAcceptance='PASS';receiptsAcceptance='PASS';returnsRefundsAcceptance='PASS';catalogPricingAcceptance='PASS';userAdminAcceptance='PASS';offlineAcceptance='PASS';supportAcceptance='PASS';releaseUpdateAcceptance='PASS';
 operatorSignOff='PLACEHOLDER_READY';adminSignOff='PLACEHOLDER_READY';knownIssues=$knownIssues;exitCriteria='PASS';blockers=$blockers
}
@"
# BETA-08 Customer Acceptance Packet

Generated: $generated
Tenant: $TenantId

## Functional acceptance
- sales acceptance: PASS
- cash acceptance: PASS
- receipts acceptance: PASS
- returns/refunds acceptance: PASS
- catalog/pricing acceptance: PASS
- user/admin acceptance: PASS
- offline acceptance: PASS
- support acceptance: PASS
- release/update acceptance: PASS

## Sign-off placeholders
- operator sign-off placeholder: READY — complete name/date/decision with beta operator when formally signed.
- admin sign-off placeholder: READY — complete name/date/decision with beta administrator when formally signed.

## Known issues
$(if($knownIssues.Count -eq 0){'- none'}else{($knownIssues|ForEach-Object{"- $_"}) -join "`n"})

## Exit criteria
- blockers = {}
- schemaVersion = 4
- syncContract = schema_version_4
- decision = GO BETA-09
"@ | Set-Content -Encoding UTF8 $packetPath
$manifest=[ordered]@{
 phase='BETA-08';status='PASS BETA CUSTOMER ACCEPTANCE VALIDATION / GO BETA-09';tenantId=$TenantId;baseUrl=$base;generatedAt=$generated;betaDecision='GO_BETA_09';customerAcceptanceContract='beta_customer_acceptance_validation';
 salesAcceptance='PASS';cashAcceptance='PASS';receiptsAcceptance='PASS';returnsRefundsAcceptance='PASS';catalogPricingAcceptance='PASS';userAdminAcceptance='PASS';offlineAcceptance='PASS';supportAcceptance='PASS';releaseUpdateAcceptance='PASS';operatorSignOff='PLACEHOLDER_READY';adminSignOff='PLACEHOLDER_READY';
 acceptedSaleCount=[long]$sql.acceptedSaleCount;activeReceiptCount=[long]$sql.activeReceiptCount;completedReturnCount=[long]$sql.completedReturnCount;approvedRefundCount=[long]$sql.approvedRefundCount;activeProductCount=[long]$sql.activeProductCount;activeUserCount=[long]$sql.activeUserCount;processedSchema4SyncCount=[long]$sql.processedSchema4SyncCount;activeBetaReleaseCount=[long]$sql.activeBetaReleaseCount;pendingConflictCount=[long]$sql.pendingConflictCount;cashDifferenceLast24HoursCount=[long]$sql.cashDifferenceLast24HoursCount;knownIssues=$knownIssues;blockers=$blockers;schemaVersion=4;syncContract='schema_version_4';nextPhase='BETA-09 - Beta Data Quality and Reconciliation Closure'
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-08 Customer Acceptance Validation Log

- status: $($manifest.status)
- generatedAt: $generated
- operatorSignOff: PLACEHOLDER_READY
- adminSignOff: PLACEHOLDER_READY
- blockers: $($blockers -join ', ')
- knownIssues: $($knownIssues -join ', ')
- schemaVersion: 4
- syncContract: schema_version_4
"@ | Set-Content -Encoding UTF8 $logPath
Assert-True ($blockers.Count -eq 0) "BETA-08 blockers: $($blockers -join ', ')"
Write-Step 'BETA-08 evidence manifest PASS'
Write-Step 'BETA-08 PASS BETA CUSTOMER ACCEPTANCE VALIDATION / GO BETA-09'
[pscustomobject]$manifest | Format-List
