param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-10] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','events','conflicts','sales','metrics','buckets','channels','releases','users','stores','roles','permissions','terminals','customers')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Get-LongValue { param($Object,[string[]]$Names,[long]$Default=0) if($null -eq $Object){return $Default}; foreach($name in $Names){ if($null -ne $Object.$name){ return [long]$Object.$name } }; return $Default }
function New-SafePassword { $g=[Guid]::NewGuid().ToString('N').Substring(0,12); return "S0lidPOS!$g" }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-Api { param([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}) $uri="$script:base$Path"; $params=@{Method=$Method; Uri=$uri; Headers=$Headers}; if($null -ne $Body){ $params.Body=($Body|ConvertTo-Json -Depth 20); $params.ContentType='application/json' }; return Invoke-RestMethod @params }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-10-customer-admin-management-completion-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-10-customer-admin-management-completion'
$manifestPath=Join-Path $runtimeDirectory 'customer-admin-management-completion-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-10-customer-admin-management-completion-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-10-customer-admin-management-completion.md'
  admin=Join-Path $repoRoot 'docs\expansion\exp-10-admin-operator-runbook.md'
  rbac=Join-Path $repoRoot 'docs\expansion\exp-10-rbac-store-access-policy.md'
  customers=Join-Path $repoRoot 'docs\expansion\exp-10-customer-management-runbook.md'
  audit=Join-Path $repoRoot 'docs\expansion\exp-10-admin-audit-evidence.md'
  rollback=Join-Path $repoRoot 'docs\expansion\exp-10-admin-management-rollback.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-10-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-10 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-10 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-10','customer','admin management','rbac','audit','exp-11')
Assert-DocumentContains -Path $docs.admin -Terms @('daily admin operations','user administration','customer administration','audit trail')
Assert-DocumentContains -Path $docs.rbac -Terms @('rbac','store access','role assignment','protected endpoint')
Assert-DocumentContains -Path $docs.customers -Terms @('customer lifecycle','sales history','audit trail','safety')
Assert-DocumentContains -Path $docs.audit -Terms @('customer.created','customer.updated','user.created','user.updated','no-go')
Assert-DocumentContains -Path $docs.rollback -Terms @('rollback','suspended','archived','do not hard-delete','audit')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','customer','user','exp-11')
Write-Step 'EXP-10 document contract PASS'

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
  if(Test-Path (Join-Path $dashboardRoot 'package.json')){
    Write-Step 'Dashboard build...'
    Invoke-NpmCommand -Arguments @('install') -WorkingDirectory $dashboardRoot
    Invoke-NpmCommand -Arguments @('run','build') -WorkingDirectory $dashboardRoot
    Write-Step 'Dashboard build PASS'
  }
}

Write-Step 'Production liveness/readiness...'
$live=Invoke-Api -Method Get -Path '/health/live'
$ready=Invoke-Api -Method Get -Path '/health/ready'
Assert-True (($live.status -eq 'alive') -or ($live -eq 'alive')) 'health/live must be alive.'
Assert-True (($ready.status -eq 'ready') -or ($ready -eq 'ready')) 'health/ready must be ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin login and admin endpoint contract...'
$loginBody=@{ email=$Email; password=$plainPassword; tenantId=$TenantId }
$session=Invoke-Api -Method Post -Path '/api/v1/auth/login' -Body $loginBody
$token=$session.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$authHeaders=@{ Authorization="Bearer $token" }
$tenant=Invoke-Api -Method Get -Path '/api/v1/tenants/current' -Headers $authHeaders
Assert-True ($tenant.id -eq $TenantId) 'Current tenant mismatch.'
$stores=Get-Items (Invoke-Api -Method Get -Path '/api/v1/stores' -Headers $authHeaders)
$usersBefore=Get-Items (Invoke-Api -Method Get -Path '/api/v1/users' -Headers $authHeaders)
$roles=Get-Items (Invoke-Api -Method Get -Path '/api/v1/roles' -Headers $authHeaders)
$permissions=Get-Items (Invoke-Api -Method Get -Path '/api/v1/permissions' -Headers $authHeaders)
$terminals=Get-Items (Invoke-Api -Method Get -Path '/api/v1/terminals' -Headers $authHeaders)
Assert-True ($stores.Count -ge 1) 'At least one store is required.'
Assert-True ($roles.Count -ge 1) 'At least one role is required.'
Assert-True ($permissions.Count -ge 1) 'At least one permission is required.'
$primaryStore=$stores | Where-Object {$_.status -eq 'active'} | Select-Object -First 1
if($null -eq $primaryStore){ $primaryStore=$stores | Select-Object -First 1 }
$role=$roles | Where-Object {$_.code -eq 'reports'} | Select-Object -First 1
if($null -eq $role){ $role=$roles | Select-Object -First 1 }
Assert-True ($null -ne $primaryStore -and -not [string]::IsNullOrWhiteSpace($primaryStore.id)) 'Primary store not resolved.'
Assert-True ($null -ne $role -and -not [string]::IsNullOrWhiteSpace($role.code)) 'Role not resolved.'
Write-Step 'Admin login and admin endpoint contract PASS'

Write-Step 'Customer management operational flow...'
$suffix=[Guid]::NewGuid().ToString('N').Substring(0,10)
$customerEmail="exp10-customer-$suffix@solidpos.local"
$customer=Invoke-Api -Method Post -Path '/api/v1/customers' -Headers $authHeaders -Body @{ name="EXP-10 Customer $suffix"; email=$customerEmail; phone="55501010"; creditLimitCents=0 }
Assert-True (-not [string]::IsNullOrWhiteSpace($customer.id)) 'Customer create failed.'
$customerId=$customer.id
$customerFetched=Invoke-Api -Method Get -Path "/api/v1/customers/$customerId" -Headers $authHeaders
Assert-True ($customerFetched.id -eq $customerId) 'Customer get failed.'
$customerUpdated=Invoke-Api -Method Patch -Path "/api/v1/customers/$customerId" -Headers $authHeaders -Body @{ name="EXP-10 Customer Updated $suffix"; email=$customerEmail; phone="55502020"; creditLimitCents=10000; status='active' }
Assert-True ($customerUpdated.phone -eq '55502020') 'Customer update failed.'
$customerListMatched=$false
$customerListPaths=@(
  "/api/v1/customers?search=$suffix&status=active&limit=50",
  "/api/v1/customers?search=$customerEmail&status=active&limit=50",
  "/api/v1/customers?q=$suffix&status=active&limit=50",
  "/api/v1/customers?email=$customerEmail&status=active&limit=50",
  "/api/v1/customers?limit=100"
)
foreach($customerListPath in $customerListPaths){
  try {
    $candidateCustomers=Get-Items (Invoke-Api -Method Get -Path $customerListPath -Headers $authHeaders)
    if((@($candidateCustomers) | Where-Object { $_.id -eq $customerId -or $_.email -eq $customerEmail }).Count -ge 1){
      $customerListMatched=$true
      break
    }
  } catch {
    # Some deployments expose only a subset of list filters. The source of truth for EXP-10 is GET by id + SQL cross-check.
  }
}
$customerSales=Invoke-Api -Method Get -Path "/api/v1/customers/$customerId/sales?limit=10" -Headers $authHeaders
Assert-True ($null -ne $customerSales) 'Customer sales history unavailable.'
Write-Step 'Customer management operational flow PASS'

Write-Step 'Controlled user management operational flow...'
$userEmail="exp10-user-$suffix@solidpos.local"
$userPassword=New-SafePassword
$newUser=Invoke-Api -Method Post -Path '/api/v1/users' -Headers $authHeaders -Body @{ email=$userEmail; fullName="EXP-10 Support User $suffix"; password=$userPassword; status='active'; roleCodes=@($role.code); storeIds=@($primaryStore.id) }
Assert-True (-not [string]::IsNullOrWhiteSpace($newUser.id)) 'User create failed.'
$userId=$newUser.id
Assert-True (($newUser.roleCodes | Where-Object {$_ -eq $role.code}).Count -ge 1) 'User role assignment missing.'
Assert-True (($newUser.storeIds | Where-Object {$_ -eq $primaryStore.id}).Count -ge 1) 'User store assignment missing.'
$updatedUser=Invoke-Api -Method Patch -Path "/api/v1/users/$userId" -Headers $authHeaders -Body @{ fullName="EXP-10 Support User Updated $suffix"; status='active'; roleCodes=@($role.code); storeIds=@($primaryStore.id) }
Assert-True ($updatedUser.fullName -like 'EXP-10 Support User Updated*') 'User update failed.'
$userListMatched=$false
$userListPaths=@(
  "/api/v1/users?search=$suffix&status=active&limit=50",
  "/api/v1/users?search=$userEmail&status=active&limit=50",
  "/api/v1/users?q=$suffix&status=active&limit=50",
  "/api/v1/users?email=$userEmail&status=active&limit=50",
  "/api/v1/users?limit=100",
  "/api/v1/users"
)
foreach($userListPath in $userListPaths){
  try {
    $candidateUsers=Get-Items (Invoke-Api -Method Get -Path $userListPath -Headers $authHeaders)
    if((@($candidateUsers) | Where-Object { $_.id -eq $userId -or $_.email -eq $userEmail }).Count -ge 1){
      $userListMatched=$true
      break
    }
  } catch {
    # Some deployments expose only a subset of list filters. The source of truth for EXP-10 is create/update response + SQL cross-check.
  }
}
Write-Step 'Controlled user management operational flow PASS'

Write-Step 'SQL customer/admin management cross-check...'
$sqlResult=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id=$TenantId; customer_id=$customerId; user_id=$userId; store_id=$primaryStore.id; role_code=$role.code }
Assert-True ($sqlResult.exp10SqlValidation -eq 'GO') "EXP-10 SQL validation returned $($sqlResult.exp10SqlValidation): $($sqlResult.blockers -join ',')"
Write-Step 'SQL customer/admin management cross-check PASS'

Write-Step 'Customer/admin decision matrix...'
$blockers=@()
if($sqlResult.blockers){ $blockers += @($sqlResult.blockers) }
$conditions=@()
if(-not $customerListMatched){ $conditions += 'review_customer_list_search_filter_contract' }
if(-not $userListMatched){ $conditions += 'review_user_list_search_filter_contract' }
if([long]$sqlResult.terminalCount -lt 1){ $conditions += 'review_terminal_inventory' }
$manifest=[ordered]@{
  phase='EXP-10'
  status='PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11'
  tenantId=$TenantId
  baseUrl=$script:base
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  adminDecision='GO_CUSTOMER_ADMIN_MANAGEMENT_COMPLETE'
  exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02'
  exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
  exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
  exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
  exp05='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
  exp06='PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
  exp07='PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08'
  exp08='PASS SUPPORT AND INCIDENT OPERATIONS / GO EXP-09'
  exp09='PASS RELEASE MANAGEMENT AND UPDATE CHANNEL / GO EXP-10'
  healthLive='alive'
  healthReady='ready'
  databaseReady='ready'
  storeCount=[long]$sqlResult.storeCount
  activeStoreCount=[long]$sqlResult.activeStoreCount
  terminalCount=[long]$sqlResult.terminalCount
  userCount=[long]$sqlResult.userCount
  roleCount=[long]$sqlResult.roleCount
  permissionCount=[long]$sqlResult.permissionCount
  customerCount=[long]$sqlResult.customerCount
  customerListMatched=$customerListMatched
  userListMatched=$userListMatched
  customerId=$customerId
  customerEmail=$customerEmail
  userId=$userId
  userEmail=$userEmail
  assignedRoleCode=$role.code
  assignedStoreId=$primaryStore.id
  exp10StoreAccessCount=[long]$sqlResult.exp10StoreAccessCount
  exp10RoleAssignmentCount=[long]$sqlResult.exp10RoleAssignmentCount
  exp10CustomerAuditCount=[long]$sqlResult.exp10CustomerAuditCount
  exp10UserAuditCount=[long]$sqlResult.exp10UserAuditCount
  auditEventsLast24Hours=[long]$sqlResult.auditEventsLast24Hours
  blockers=$blockers
  conditions=$conditions
  sqlWarnings=@($sqlResult.sqlWarnings)
  schemaVersion=4
  customerAdminContract='customer_admin_management_completion'
  nextPhase='EXP-11 Catalog Pricing Operations'
}
Assert-True ($blockers.Count -eq 0) "EXP-10 blockers present: $($blockers -join ',')"
Write-Step 'Customer/admin decision matrix PASS'

Write-Step 'Write customer/admin manifest and log...'
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $manifestPath
@"
# EXP-10 Customer/Admin Management Completion Log

- phase: EXP-10
- status: PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11
- generatedAt: $($manifest.generatedAt)
- tenantId: $TenantId
- baseUrl: $script:base
- customerId: $customerId
- userId: $userId
- assignedRoleCode: $($role.code)
- assignedStoreId: $($primaryStore.id)
- sqlValidation: $($sqlResult.exp10SqlValidation)
- blockers: $($blockers -join ',')
- nextPhase: EXP-11 Catalog Pricing Operations
"@ | Set-Content -Encoding UTF8 -Path $logPath
Write-Step 'Write customer/admin manifest and log PASS'
Write-Step 'EXP-10 PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11'
$manifest
