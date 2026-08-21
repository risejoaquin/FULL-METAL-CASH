param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Message) Write-Host "[BETA-01] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString {
    param([securestring]$SecureValue)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
function Invoke-CheckedCommand {
    param([string]$Name,[scriptblock]$Command)
    $global:LASTEXITCODE=0
    & $Command
    if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }
    $global:LASTEXITCODE=0
}
function Invoke-NpmCommand {
    param([string[]]$Arguments,[string]$WorkingDirectory)
    Push-Location $WorkingDirectory
    try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } }
    finally { Pop-Location }
}
function Get-Items {
    param($Response)
    if($null -eq $Response){ return @() }
    if($Response -is [System.Array]){ return @($Response) }
    foreach($name in @('items','data','results','events','stores','users','roles','permissions','terminals','customers','categories','products','priceLists','channels')){
        if($null -ne $Response.$name){ return @($Response.$name) }
    }
    return @($Response)
}
function Get-TextValue {
    param($Object,[string[]]$Names)
    foreach($name in $Names){ if($null -ne $Object -and $null -ne $Object.$name){ return [string]$Object.$name } }
    return ''
}
function Assert-DocumentContains {
    param([string]$Path,[string[]]$Terms)
    Assert-True (Test-Path $Path) "Required BETA-01 document missing: $Path"
    $content=(Get-Content -Raw -Path $Path).ToLowerInvariant()
    foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" }
}
function Invoke-Api {
    param([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{})
    $uri="$script:base$Path"
    $params=@{ Method=$Method; Uri=$uri; Headers=$Headers; TimeoutSec=30 }
    if($null -ne $Body){ $params.Body=($Body | ConvertTo-Json -Depth 40); $params.ContentType='application/json' }
    return Invoke-RestMethod @params
}
function Invoke-DbJsonFile {
    param([string]$SqlPath,[hashtable]$Variables)
    $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path
    $fileName=Split-Path -Leaf $SqlPath
    $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1')
    foreach($key in $Variables.Keys){ $args += @('-v',"$key=$($Variables[$key])") }
    $args += @('-f',"/sql/$fileName")
    $global:LASTEXITCODE=0
    $output=docker @args
    if($LASTEXITCODE -ne 0){ throw "DB JSON file command failed for $SqlPath." }
    $global:LASTEXITCODE=0
    $json=($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1)
    Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'
    return ($json | ConvertFrom-Json)
}

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'beta-01-controlled-commercial-beta-onboarding-check.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\beta-01-controlled-commercial-beta-onboarding'
$manifestPath=Join-Path $runtimeDirectory 'beta-01-onboarding-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\beta\logs'
$logPath=Join-Path $logDirectory 'beta-01-controlled-commercial-beta-onboarding-log.md'
$docs=@{
    phase=Join-Path $repoRoot 'docs\beta\beta-01-controlled-commercial-beta-onboarding.md'
    store=Join-Path $repoRoot 'docs\beta\beta-01-store-onboarding-checklist.md'
    support=Join-Path $repoRoot 'docs\beta\beta-01-support-contact-matrix.md'
    acceptance=Join-Path $repoRoot 'docs\beta\beta-01-customer-acceptance-checklist.md'
    goNoGo=Join-Path $repoRoot 'docs\beta\beta-01-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'BETA-01 SQL validator is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'BETA-01 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('beta-01','controlled commercial beta onboarding','schemaVersion = 4','sql cross-check')
Assert-DocumentContains -Path $docs.store -Terms @('store onboarding checklist','terminal','admin','price list','audit')
Assert-DocumentContains -Path $docs.support -Terms @('support contact matrix','sev-1','sev-2','evidence')
Assert-DocumentContains -Path $docs.acceptance -Terms @('customer acceptance checklist','operator sign-off','production validation')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go / no-go','pending user validation','go beta-02')
Write-Step 'BETA-01 document contract PASS'

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

if(-not $SkipDashboardBuild -and (Test-Path (Join-Path $dashboardRoot 'package.json'))){
    Write-Step 'Dashboard build...'
    if(Test-Path (Join-Path $dashboardRoot 'package-lock.json')){ Invoke-NpmCommand -Arguments @('ci') -WorkingDirectory $dashboardRoot }
    else { Invoke-NpmCommand -Arguments @('install') -WorkingDirectory $dashboardRoot }
    Invoke-NpmCommand -Arguments @('run','build') -WorkingDirectory $dashboardRoot
    Write-Step 'Dashboard build PASS'
}

Write-Step 'Production liveness/readiness...'
$live=Invoke-Api -Method Get -Path '/health/live'
$ready=Invoke-Api -Method Get -Path '/health/ready'
Assert-True (($live.status -eq 'alive') -or ($live -eq 'alive')) 'health/live must be alive.'
Assert-True (($ready.status -eq 'ready') -or ($ready -eq 'ready')) 'health/ready must be ready.'
Assert-True (($ready.database -eq 'ready') -or ($ready.databaseReady -eq $true) -or ($ready.database.ready -eq $true)) 'database readiness must be ready.'
Write-Step 'Production liveness/readiness PASS'

Write-Step 'Admin bootstrap and protected endpoint contract...'
$session=Invoke-Api -Method Post -Path '/api/v1/auth/login' -Body @{ email=$Email; password=$plainPassword; tenantId=$TenantId }
$token=$session.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$authHeaders=@{ Authorization="Bearer $token" }
$tenant=Invoke-Api -Method Get -Path '/api/v1/tenants/current' -Headers $authHeaders
Assert-True ((Get-TextValue -Object $tenant -Names @('id','tenantId')) -eq $TenantId) 'Current tenant mismatch.'
$stores=Get-Items (Invoke-Api -Method Get -Path '/api/v1/stores' -Headers $authHeaders)
$users=Get-Items (Invoke-Api -Method Get -Path '/api/v1/users' -Headers $authHeaders)
$roles=Get-Items (Invoke-Api -Method Get -Path '/api/v1/roles' -Headers $authHeaders)
$permissions=Get-Items (Invoke-Api -Method Get -Path '/api/v1/permissions' -Headers $authHeaders)
$terminals=Get-Items (Invoke-Api -Method Get -Path '/api/v1/terminals' -Headers $authHeaders)
$customers=Get-Items (Invoke-Api -Method Get -Path '/api/v1/customers?status=active&limit=25' -Headers $authHeaders)
$catalog=Invoke-Api -Method Get -Path '/api/v1/tenant/catalog' -Headers $authHeaders
$channels=Get-Items (Invoke-Api -Method Get -Path '/api/v1/updates/channels' -Headers $authHeaders)
$metrics=Invoke-Api -Method Get -Path '/api/v1/observability/metrics' -Headers $authHeaders
$auditEvents=Get-Items (Invoke-Api -Method Get -Path '/api/v1/audit/events?limit=25' -Headers $authHeaders)
Assert-True ($stores.Count -ge 1) 'At least one store is required by the protected endpoint contract.'
Assert-True ($users.Count -ge 1) 'At least one user is required by the protected endpoint contract.'
Assert-True ($roles.Count -ge 1) 'At least one role is required.'
Assert-True ($permissions.Count -ge 1) 'At least one permission is required.'
Assert-True ($terminals.Count -ge 1) 'At least one terminal is required.'
Assert-True ($null -ne $catalog) 'Runtime catalog endpoint did not return data.'
Assert-True ($null -ne $metrics) 'Observability metrics endpoint did not return data.'
$channelNames=@($channels | ForEach-Object { (Get-TextValue -Object $_ -Names @('code','name','channel')).ToLowerInvariant() })
Assert-True ($channelNames -contains 'beta') 'Update channel contract must expose beta channel.'
Write-Step 'Admin bootstrap and protected endpoint contract PASS'

Write-Step 'SQL onboarding source-of-truth cross-check...'
$sql=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id=$TenantId; admin_email=$Email }
$blockers=@($sql.blockers)
$conditions=@($sql.conditions)
Assert-True ($sql.beta01SqlValidation -eq 'GO') "BETA-01 SQL validation failed: $($blockers -join ', ')"
Assert-True ([long]$sql.activeAdminCount -eq 1) 'SQL must find exactly one active unlocked admin matching the login email.'
Assert-True ([long]$sql.adminRoleAssignmentCount -ge 1) 'Admin requires at least one role assignment.'
Assert-True ([long]$sql.adminStoreAccessCount -ge 1) 'Admin requires store access for controlled onboarding.'
Assert-True ([long]$sql.activeCustomerCount -ge 1) 'At least one active beta customer profile is required.'
Assert-True ([long]$sql.pendingConflictCount -eq 0) 'Pending sync conflicts block BETA-01.'
Assert-True ([long]$sql.legacySchemaEventCount -eq 0) 'Legacy schema sync events block BETA-01.'
Write-Step 'SQL onboarding source-of-truth cross-check PASS'

Write-Step 'Build BETA-01 decision and evidence manifest...'
if($customers.Count -lt 1){ $conditions += 'customer_list_runtime_visibility_review' }
if($auditEvents.Count -lt 1){ $conditions += 'audit_endpoint_recent_visibility_review' }
$runtimeCatalogItems=Get-Items $catalog
if($runtimeCatalogItems.Count -lt 1){ $conditions += 'runtime_catalog_visibility_review' }
$conditions=@($conditions | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)
Assert-True ($blockers.Count -eq 0) "BETA-01 blockers detected: $($blockers -join ', ')"

$manifest=[ordered]@{
    phase='BETA-01'
    status='PASS CONTROLLED COMMERCIAL BETA ONBOARDING / GO BETA-02'
    tenantId=$TenantId
    baseUrl=$script:base
    generatedAt=(Get-Date).ToUniversalTime().ToString('o')
    betaDecision='GO_BETA_02'
    onboardingContract='controlled_commercial_beta_onboarding'
    healthLive='alive'
    healthReady='ready'
    databaseReady='ready'
    endpointStoreCount=$stores.Count
    endpointUserCount=$users.Count
    endpointRoleCount=$roles.Count
    endpointPermissionCount=$permissions.Count
    endpointTerminalCount=$terminals.Count
    endpointCustomerSampleCount=$customers.Count
    endpointUpdateChannels=$channelNames
    activeStoreCount=[long]$sql.activeStoreCount
    activeTerminalCount=[long]$sql.activeTerminalCount
    activeTerminalWithActiveStoreCount=[long]$sql.activeTerminalWithActiveStoreCount
    activeAdminCount=[long]$sql.activeAdminCount
    adminRoleAssignmentCount=[long]$sql.adminRoleAssignmentCount
    adminStoreAccessCount=[long]$sql.adminStoreAccessCount
    activeCustomerCount=[long]$sql.activeCustomerCount
    activeCategoryCount=[long]$sql.activeCategoryCount
    activeSellableProductCount=[long]$sql.activeSellableProductCount
    activePriceListCount=[long]$sql.activePriceListCount
    activeMxnPriceCount=[long]$sql.activeMxnPriceCount
    activeTenantReleaseCount=[long]$sql.activeTenantReleaseCount
    velopackUniversalReleaseCount=[long]$sql.velopackUniversalReleaseCount
    auditEventCount=[long]$sql.auditEventCount
    retryPendingSync=[long]$sql.retryPendingSync
    deadLetterSync=[long]$sql.deadLetterSync
    pendingConflictCount=[long]$sql.pendingConflictCount
    legacySchemaEventCount=[long]$sql.legacySchemaEventCount
    blockers=$blockers
    conditions=$conditions
    schemaVersion=4
    syncContract='schema_version_4'
    nextPhase='BETA-02 — Beta Tenant Provisioning and Separation Hardening'
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $manifestPath
$log=@"
# BETA-01 Controlled Commercial Beta Onboarding Log

- phase: BETA-01
- status: PASS CONTROLLED COMMERCIAL BETA ONBOARDING / GO BETA-02
- generatedAt: $($manifest.generatedAt)
- tenantId: $TenantId
- baseUrl: $script:base
- activeStoreCount: $($manifest.activeStoreCount)
- activeTerminalCount: $($manifest.activeTerminalCount)
- activeAdminCount: $($manifest.activeAdminCount)
- adminRoleAssignmentCount: $($manifest.adminRoleAssignmentCount)
- adminStoreAccessCount: $($manifest.adminStoreAccessCount)
- activeCustomerCount: $($manifest.activeCustomerCount)
- activeSellableProductCount: $($manifest.activeSellableProductCount)
- activePriceListCount: $($manifest.activePriceListCount)
- activeMxnPriceCount: $($manifest.activeMxnPriceCount)
- activeTenantReleaseCount: $($manifest.activeTenantReleaseCount)
- velopackUniversalReleaseCount: $($manifest.velopackUniversalReleaseCount)
- pendingConflictCount: $($manifest.pendingConflictCount)
- legacySchemaEventCount: $($manifest.legacySchemaEventCount)
- blockers: $($blockers -join ',')
- conditions: $($conditions -join ',')
- schemaVersion: 4
- syncContract: schema_version_4
- nextPhase: BETA-02 — Beta Tenant Provisioning and Separation Hardening
"@
$log | Set-Content -Encoding UTF8 -Path $logPath
Write-Step 'BETA-01 evidence manifest PASS'
Write-Step 'BETA-01 PASS CONTROLLED COMMERCIAL BETA ONBOARDING / GO BETA-02'
$manifest
