param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step([string]$m){ Write-Host "[BETA-02] $m" }
function Assert-True([bool]$c,[string]$m){ if(-not $c){ throw $m } }
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;&$c;if($LASTEXITCODE -ne 0){throw "$n failed with exit code $LASTEXITCODE"};$global:LASTEXITCODE=0}
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}){$p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=30};if($null-ne $Body){$p.Body=$Body|ConvertTo-Json -Depth 30;$p.ContentType='application/json'};Invoke-RestMethod @p}
function Items($r){if($null-eq$r){return @()};if($r-is[System.Array]){return @($r)};foreach($n in @('items','data','results','stores','users','terminals','customers')){if($null-ne$r.$n){return @($r.$n)}};return @($r)}
function Ids($items){@($items|ForEach-Object{if($_.id){[string]$_.id}elseif($_.storeId){[string]$_.storeId}elseif($_.userId){[string]$_.userId}elseif($_.terminalId){[string]$_.terminalId}elseif($_.customerId){[string]$_.customerId}}|Where-Object{$_})}
function Invoke-DbJson([string]$SqlPath,[hashtable]$Vars){$d=(Resolve-Path(Split-Path -Parent $SqlPath)).Path;$f=Split-Path -Leaf $SqlPath;$a=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${d}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$a+=@('-v',"$k=$($Vars[$k])")};$a+=@('-f',"/sql/$f");$global:LASTEXITCODE=0;$o=docker @a;if($LASTEXITCODE-ne0){throw 'DB SQL validator failed.'};$j=($o|Where-Object{$_}|Select-Object -Last 1);$j|ConvertFrom-Json}
function HttpStatus([string]$Path,[hashtable]$Headers){try{Invoke-RestMethod -Method Get -Uri "$script:base$Path" -Headers $Headers -TimeoutSec 30|Out-Null;return 200}catch{if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
function Assert-Owned([string[]]$ApiIds,$DbIds,[string]$Name){$owned=@($DbIds|ForEach-Object{[string]$_});foreach($id in $ApiIds){Assert-True ($owned -contains $id) "$Name leaked foreign id $id"}}

$script:base=$BaseUrl.TrimEnd('/');$plain=Convert-Secret $Password;$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=Resolve-Path(Join-Path $scriptRoot '..\..');$sln=Join-Path $repo 'solidpos-platform.sln';$sqlPath=Join-Path $scriptRoot 'beta-02-tenant-provisioning-separation-hardening-check.sql';$runtime=Join-Path $repo '.runtime\beta-02-tenant-provisioning-separation-hardening';$manifestPath=Join-Path $runtime 'beta-02-separation-manifest.json';$logPath=Join-Path $repo 'docs\beta\logs\beta-02-tenant-provisioning-separation-hardening-log.md';New-Item -ItemType Directory -Force $runtime,(Split-Path $logPath)|Out-Null

Write-Step 'Repository and source hardening guardrails...'
Assert-True (Test-Path $sln) 'solution missing';Assert-True(Test-Path $sqlPath)'BETA-02 SQL missing';Assert-True($DatabaseUrl -match '^postgres(ql)?://')'DATABASE_URL must be PostgreSQL.'
$repoSource=Get-Content -Raw (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Provisioning\PostgreSqlProductionProvisioningRepository.cs')
Assert-True ($repoSource.Contains('existing.RequestHash, requestHash')) 'Idempotency request-hash mismatch hardening missing.'
Write-Step 'Repository and source hardening guardrails PASS'
Write-Step 'Local secret scan...';Invoke-Checked 'secret scan' {& (Join-Path $repo 'scripts\security\scan-local-secrets.ps1') -Root $repo};Write-Step 'Local secret scan PASS'
Write-Step 'dotnet restore...';Invoke-Checked 'dotnet restore' {dotnet restore $sln};Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...';Invoke-Checked 'dotnet build' {dotnet build $sln --no-restore};Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...';Invoke-Checked 'dotnet test' {dotnet test $sln --no-build};Write-Step 'dotnet test PASS'

Write-Step 'Production provisioning status and authentication...'
$status=Invoke-Api Get '/api/v1/provisioning/status';Assert-True ($status.enabled -eq $true) 'Production provisioning must be enabled.';Assert-True ($status.configured -eq $true) 'Production provisioning key must be configured.';Assert-True ([string]$status.requiredHeader -eq 'X-SolidPOS-Provision-Key') 'Unexpected provisioning header contract.'
$session=Invoke-Api Post '/api/v1/auth/login' @{email=$Email;password=$plain;tenantId=$TenantId};Assert-True(-not[string]::IsNullOrWhiteSpace($session.accessToken))'admin login missing access token';$h=@{Authorization="Bearer $($session.accessToken)"}
$current=Invoke-Api Get '/api/v1/tenants/current' $null $h;Assert-True(([string]$current.id -eq $TenantId)-or([string]$current.tenantId -eq $TenantId))'tenant context mismatch'
Write-Step 'Production provisioning status and authentication PASS'

Write-Step 'SQL tenant provisioning source-of-truth...'
$sql=Invoke-DbJson $sqlPath @{tenant_id=$TenantId;admin_email=$Email};$blockers=@($sql.blockers);Assert-True($blockers.Count-eq0)"BETA-02 SQL blockers: $($blockers -join ', ')";$conditions=@($sql.conditions);Write-Step 'SQL tenant provisioning source-of-truth PASS'

Write-Step 'Tenant-scoped list isolation...'
$stores=Items(Invoke-Api Get '/api/v1/stores' $null $h);$users=Items(Invoke-Api Get '/api/v1/users' $null $h);$terminals=Items(Invoke-Api Get '/api/v1/terminals' $null $h);$customers=Items(Invoke-Api Get '/api/v1/customers?limit=100' $null $h)
Assert-Owned (Ids $stores) $sql.targetStoreIds 'stores';Assert-Owned (Ids $users) $sql.targetUserIds 'users';Assert-Owned (Ids $terminals) $sql.targetTerminalIds 'terminals';Assert-Owned (Ids $customers) $sql.targetCustomerIds 'customers'
$catalog=Invoke-Api Get '/api/v1/tenant/catalog' $null $h;$catalogJson=$catalog|ConvertTo-Json -Depth 50
if($sql.foreignProductId){Assert-True(-not $catalogJson.Contains([string]$sql.foreignProductId))'catalog leaked a foreign product id'}
Write-Step 'Tenant-scoped list isolation PASS'

Write-Step 'Cross-tenant negative reads...'
$negative=@{}
if($sql.foreignCustomerId){$negative.foreignCustomerStatus=HttpStatus "/api/v1/customers/$($sql.foreignCustomerId)" $h;Assert-True($negative.foreignCustomerStatus-eq404)'Foreign customer must return 404.'}
if($sql.foreignSaleId){$negative.foreignSaleStatus=HttpStatus "/api/v1/sales/$($sql.foreignSaleId)" $h;Assert-True($negative.foreignSaleStatus-eq404)'Foreign sale must return 404.'}
Write-Step 'Cross-tenant negative reads PASS'

$manifest=[ordered]@{phase='BETA-02';status='PASS BETA TENANT PROVISIONING SEPARATION HARDENING / GO BETA-03';tenantId=$TenantId;baseUrl=$script:base;generatedAt=(Get-Date).ToUniversalTime().ToString('o');betaDecision='GO_BETA_03';provisioningEnabled=$status.enabled;provisioningConfigured=$status.configured;completedBootstrapRunCount=[long]$sql.completedBootstrapRunCount;seededRoleCount=[long]$sql.seededRoleCount;seededPermissionAssignmentCount=[long]$sql.seededPermissionAssignmentCount;adminOwnerAssignmentCount=[long]$sql.adminOwnerAssignmentCount;adminStoreAccessCount=[long]$sql.adminStoreAccessCount;activeStoreCount=[long]$sql.activeStoreCount;activeTerminalCount=[long]$sql.activeTerminalCount;activeProductCount=[long]$sql.activeProductCount;activePriceListCount=[long]$sql.activePriceListCount;tenantReleaseCount=[long]$sql.tenantReleaseCount;foreignTenantId=$sql.foreignTenantId;foreignFixtureCoverage=[ordered]@{stores=[long]$sql.foreignStoreCount;users=[long]$sql.foreignUserCount;terminals=[long]$sql.foreignTerminalCount;customers=[long]$sql.foreignCustomerCount;products=[long]$sql.foreignProductCount};rlsEnabledCoreTableCount=[long]$sql.rlsEnabledCoreTableCount;rlsTenantPolicyCount=[long]$sql.rlsTenantPolicyCount;crossTenantNegativeTests=$negative;conditions=$conditions;blockers=@();schemaVersion=4;syncContract='schema_version_4';nextPhase='BETA-03 - Beta Store Operations Validation'}
$manifest|ConvertTo-Json -Depth 20|Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-02 Tenant Provisioning and Separation Hardening Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- completedBootstrapRunCount: $($manifest.completedBootstrapRunCount)
- seededRoleCount: $($manifest.seededRoleCount)
- adminOwnerAssignmentCount: $($manifest.adminOwnerAssignmentCount)
- activeStoreCount: $($manifest.activeStoreCount)
- activeTerminalCount: $($manifest.activeTerminalCount)
- foreignTenantId: $($manifest.foreignTenantId)
- rlsEnabledCoreTableCount: $($manifest.rlsEnabledCoreTableCount)
- rlsTenantPolicyCount: $($manifest.rlsTenantPolicyCount)
- conditions: $($manifest.conditions -join ', ')
- blockers: {}
- schemaVersion: 4
- syncContract: schema_version_4
"@|Set-Content -Encoding UTF8 $logPath
Write-Step 'BETA-02 evidence manifest PASS';Write-Step 'BETA-02 PASS BETA TENANT PROVISIONING SEPARATION HARDENING / GO BETA-03';$manifest
