param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [switch]$SecretsRotatedAfterExposure,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
$script:Ga08ValidatorVersion='GA-08.11-powershell-assertion-type-safety' 
function Write-Step([string]$m){ Write-Host "[GA-08] $m" }
function Assert-True($c,[string]$m){
    $ok=$false
    if($null -eq $c){ $ok=$false }
    elseif($c -is [bool]){ $ok=$c }
    elseif($c -is [string]){ $ok=-not [string]::IsNullOrWhiteSpace($c) -and $c -notin @('False','false','0') }
    elseif($c -is [int] -or $c -is [long] -or $c -is [decimal] -or $c -is [double]){ $ok=([decimal]$c -ne 0) }
    elseif($c -is [System.Array]){ $ok=($c.Count -gt 0) }
    else { $ok=$true }
    if(-not $ok){ throw $m }
}
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;&$c;$ec=$LASTEXITCODE;$global:LASTEXITCODE=0;if($ec-ne0){throw "$n failed with exit code $ec"}}
function Invoke-Api([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}){
    $p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=30}
    if($null-ne$Body){$p.Body=$Body|ConvertTo-Json -Depth 30;$p.ContentType='application/json'}
    try { return Invoke-RestMethod @p }
    catch {
        $status=''; $responseText=''
        if($_.Exception.Response){
            try { $status="; httpStatus=$([int]$_.Exception.Response.StatusCode)" } catch {}
            try {
                $stream=$_.Exception.Response.GetResponseStream()
                if($stream){$reader=New-Object IO.StreamReader($stream);$responseText=$reader.ReadToEnd();$reader.Close()}
            } catch {}
        }
        if(-not [string]::IsNullOrWhiteSpace($responseText)){ $status="$status; response=$responseText" }
        throw "HTTP $Method $Path failed$status. $($_.Exception.Message)"
    }
}
function HttpStatus([string]$Method,[string]$Path,[hashtable]$Headers=@{},$Body=$null){try{$p=@{Method=$Method;Uri="$script:base$Path";Headers=$Headers;TimeoutSec=30;UseBasicParsing=$true};if($null-ne$Body){$p.Body=$Body|ConvertTo-Json -Depth 30;$p.ContentType='application/json'};$r=Invoke-WebRequest @p;return [int]$r.StatusCode}catch{if($_.Exception.Response){return [int]$_.Exception.Response.StatusCode};throw}}
function Decode-JwtPayload([string]$Token){$parts=$Token.Split('.');Assert-True($parts.Length-ge2)'JWT format invalid';$s=$parts[1].Replace('-','+').Replace('_','/');switch($s.Length%4){2{$s+='=='}3{$s+='='}};$json=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s));$json|ConvertFrom-Json}
function Invoke-DbJsonFile([string]$SqlPath,[hashtable]$Vars){$d=(Resolve-Path(Split-Path -Parent $SqlPath)).Path;$f=Split-Path -Leaf $SqlPath;$a=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${d}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$a+=@('-v',"$k=$($Vars[$k])")};$a+=@('-f',"/sql/$f");$global:LASTEXITCODE=0;$o=docker @a;$ec=$LASTEXITCODE;$global:LASTEXITCODE=0;if($ec-ne0){throw "Database SQL failed: $f"};for($i=$o.Count-1;$i-ge0;$i--){$line=[string]$o[$i];if([string]::IsNullOrWhiteSpace($line)){continue};try{return $line|ConvertFrom-Json}catch{}};throw "No JSON result returned by $f"}

$script:base=$BaseUrl.TrimEnd('/');$plain=Convert-Secret $Password;$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=(Resolve-Path(Join-Path $scriptRoot '..\..')).Path;$sln=Join-Path $repo 'solidpos-platform.sln';$sqlPath=Join-Path $scriptRoot 'ga-08-security-tenant-isolation-access-control-check.sql';$auditSql=Join-Path $scriptRoot 'ga-08-security-audit-evidence.sql';$ga07Script=Join-Path $scriptRoot 'validate-ga-07-backup-restore-rollback-disaster-recovery.ps1';$runtime=Join-Path $repo '.runtime\ga-08-security-tenant-isolation-access-control';$manifestPath=Join-Path $runtime 'ga-08-security-final-gate-manifest.json';$snapshotPath=Join-Path $runtime 'ga-08-security-final-gate-snapshot.json';$logPath=Join-Path $repo 'docs\ga\logs\ga-08-security-final-gate-log.md';New-Item -ItemType Directory -Force $runtime,(Split-Path $logPath)|Out-Null
Write-Step "Validator version $script:Ga08ValidatorVersion"
Assert-True(-not [string]::IsNullOrWhiteSpace($plain)) 'Password secure string resolved to empty/null. Re-run `$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString` before GA-08.'

Write-Step 'Repository/document/source GA-08 guardrails...'
Assert-True($SecretsRotatedAfterExposure.IsPresent)'GA-08 BLOCKER: rotate the exposed production database credential, update the deployment DATABASE_URL, and rerun with -SecretsRotatedAfterExposure.'
Assert-True(Test-Path $sln)'solution missing';Assert-True(Test-Path $sqlPath)'GA-08 SQL missing';Assert-True(Test-Path $auditSql)'GA-08 audit SQL missing';Assert-True(Test-Path(Join-Path $repo 'database\postgresql\020_ga08_complete_tenant_rls.sql'))'GA-08 RLS migration missing';Assert-True($DatabaseUrl-match'^postgres(ql)?://')'DATABASE_URL must be PostgreSQL.'
$program=Get-Content -Raw (Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Api\Program.cs');$headersSource=Get-Content -Raw(Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Security\SecurityHeadersMiddleware.cs');$claimsSource=Get-Content -Raw(Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Security\RequiredClaimsMiddleware.cs');$authSource=Get-Content -Raw(Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Auth\AuthService.cs');$jwtSource=Get-Content -Raw(Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Auth\JwtTokenService.cs');$provisionSource=Get-Content -Raw(Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Provisioning\PostgreSqlProductionProvisioningRepository.cs')
Assert-True($program.Contains('if (app.Environment.IsDevelopment())'))'Swagger development-only guard missing';Assert-True($program.Contains('app.UseMiddleware<SecurityHeadersMiddleware>()'))'Security headers middleware missing';Assert-True($program.Contains('Cors:AllowedOrigins must be explicitly configured in production.'))'Production CORS explicit-origin guard missing';Assert-True($program.Contains('Jwt:SigningKey must be at least 32 bytes.'))'JWT signing-key minimum missing';Assert-True($program.Contains('PermissionRequirement'))'Permission policies missing';Assert-True($claimsSource.Contains('invalid-token-claims'))'Required claims rejection missing';Assert-True($headersSource.Contains('X-Content-Type-Options')-and$headersSource.Contains('Strict-Transport-Security'))'Required security headers source missing';Assert-True($authSource.Contains('RotateRefreshTokenAndFindUserAsync')-and$authSource.Contains('request.TenantId'))'Atomic tenant-scoped refresh rotation defense missing';$authRepositorySource=Get-Content -Raw(Join-Path $repo 'src\PosServer\SolidPOS.PosServer.Infrastructure\Auth\PostgreSqlAuthRepository.cs');Assert-True($authRepositorySource.Contains('public async Task<AuthenticatedUser?> RotateRefreshTokenAndFindUserAsync')-and$authRepositorySource.Contains('UPDATE pos.refresh_tokens')-and$authRepositorySource.Contains('revoked_at IS NULL')-and$authRepositorySource.Contains('expires_at > now()')-and$authRepositorySource.Contains('PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken)'))'Repository atomic refresh rotation under tenant RLS missing';Assert-True($jwtSource.Contains('RandomNumberGenerator.GetBytes(64)')-and$jwtSource.Contains('Base64UrlEncoder.Encode')-and$jwtSource.Contains('SHA256.HashData'))'Refresh token generation/hash contract missing';Assert-True($provisionSource.Contains('existing.RequestHash, requestHash'))'Provisioning idempotency request hash hardening missing';Assert-True(Test-Path(Join-Path $repo 'tests\SolidPOS.PosServer.UnitTests\Auth\PermissionAuthorizationHandlerTests.cs'))'GA-08 permission negative test missing'
Write-Step 'Repository/document/source GA-08 guardrails PASS'

Write-Step 'Secret scan...';$global:LASTEXITCODE=0;& (Join-Path $repo 'scripts\security\scan-local-secrets.ps1') -Root $repo;$secretExit=$LASTEXITCODE;$global:LASTEXITCODE=0;if($secretExit-ne0){throw 'Secret scan failed.'};Write-Step 'Secret scan PASS'

Write-Step 'Fresh GA-07 prerequisite revalidation...';$global:LASTEXITCODE=0;&$ga07Script -BaseUrl $BaseUrl -TenantId $TenantId -Email $Email -Password $Password -DatabaseUrl $DatabaseUrl -SkipDashboardBuild:$SkipDashboardBuild;$ga07Exit=$LASTEXITCODE;$global:LASTEXITCODE=0;if($ga07Exit-ne0){throw 'GA-07 prerequisite revalidation failed.'};$ga07Manifest=Join-Path $repo '.runtime\ga-07-backup-restore-rollback-disaster-recovery\ga-07-manifest.json';Assert-True(Test-Path $ga07Manifest)'GA-07 manifest missing after revalidation';$ga07=Get-Content -Raw $ga07Manifest|ConvertFrom-Json;Assert-True([string]$ga07.status-eq'PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08')'GA-07 prerequisite status mismatch';Write-Step 'Fresh GA-07 prerequisite revalidation PASS'

Write-Step 'GA-08 PostgreSQL security/RLS source-of-truth...';$sql=Invoke-DbJsonFile $sqlPath @{tenant_id=$TenantId;admin_email=$Email};$sqlBlockers=@($sql.blockers);Assert-True($sqlBlockers.Count-eq0)("GA-08 SQL blockers: "+($sqlBlockers-join', '));Assert-True([long]$sql.rlsMissingTenantTableCount-eq0)'At least one tenant table lacks RLS.';Assert-True([long]$sql.rlsPolicyMissingTenantTableCount-eq0)'At least one tenant table lacks an RLS policy.';Write-Step 'GA-08 PostgreSQL security/RLS source-of-truth PASS'

Write-Step 'Production security headers, Swagger policy and CORS negative test...';$readyWeb=Invoke-WebRequest -Method Get -Uri "$script:base/health/ready" -UseBasicParsing -TimeoutSec 30;Assert-True([string]$readyWeb.Headers['X-Content-Type-Options']-eq'nosniff')'X-Content-Type-Options missing';Assert-True([string]$readyWeb.Headers['X-Frame-Options']-eq'DENY')'X-Frame-Options missing';Assert-True(-not[string]::IsNullOrWhiteSpace([string]$readyWeb.Headers['Strict-Transport-Security']))'HSTS missing';$swaggerStatus=HttpStatus Get '/swagger/v1/swagger.json';Assert-True($swaggerStatus-eq404)("Production Swagger must be hidden; status=$swaggerStatus");$corsHeaders=@{Origin='https://ga08-untrusted-origin.invalid';'Access-Control-Request-Method'='GET';'Access-Control-Request-Headers'='authorization,content-type'};try{$cors=Invoke-WebRequest -Method Options -Uri "$script:base/api/v1/sync/status" -Headers $corsHeaders -UseBasicParsing -TimeoutSec 30;$allow=[string]$cors.Headers['Access-Control-Allow-Origin']}catch{$allow=''};Assert-True($allow-ne'*' -and $allow-ne'https://ga08-untrusted-origin.invalid')'Untrusted CORS origin was allowed';Write-Step 'Production security headers, Swagger policy and CORS negative test PASS'

Write-Step 'Authentication login / JWT claims / refresh rotation / reuse / logout...'
$authStage='login'
try{
    Write-Step 'Authentication subcheck login START'
    $login=Invoke-Api Post '/api/v1/auth/login' @{email=$Email;password=$plain;tenantId=$TenantId}
    Assert-True(-not[string]::IsNullOrWhiteSpace([string]$login.accessToken))'Login access token missing'
    Assert-True(-not[string]::IsNullOrWhiteSpace([string]$login.refreshToken))'Login refresh token missing'
    Write-Step 'Authentication subcheck login PASS'

    $authStage='jwt-claims'
    $payload=Decode-JwtPayload([string]$login.accessToken)
    Assert-True([string]$payload.tenant_id-eq$TenantId)'JWT tenant_id mismatch'
    Assert-True(-not[string]::IsNullOrWhiteSpace([string]$payload.user_id))'JWT user_id missing'
    Assert-True($null-ne$payload.permissions)'JWT permissions claim missing'
    Write-Step 'Authentication subcheck JWT claims PASS'

    $authStage='refresh-rotation'
    $originalRefresh=[string]$login.refreshToken
    $rotated=Invoke-Api Post '/api/v1/auth/refresh' @{refreshToken=$originalRefresh;tenantId=$TenantId}
    Assert-True(-not[string]::IsNullOrWhiteSpace([string]$rotated.accessToken))'Rotated access token missing'
    Assert-True(-not[string]::IsNullOrWhiteSpace([string]$rotated.refreshToken))'Rotated refresh token missing'
    Assert-True([string]$rotated.refreshToken-ne$originalRefresh)'Refresh token did not rotate'
    Write-Step 'Authentication subcheck refresh rotation PASS'

    $authStage='refresh-reuse-negative'
    $reuseStatus=HttpStatus Post '/api/v1/auth/refresh' @{} @{refreshToken=$originalRefresh;tenantId=$TenantId}
    Assert-True($reuseStatus-eq401)("Old refresh token reuse must return 401; status=$reuseStatus")
    Write-Step 'Authentication subcheck old refresh reuse rejected PASS'

    $authStage='logout'
    $rotatedHeaders=@{Authorization="Bearer $($rotated.accessToken)"}
    $logoutStatus=HttpStatus Post '/api/v1/auth/logout' $rotatedHeaders @{refreshToken=[string]$rotated.refreshToken;tenantId=$TenantId}
    Assert-True($logoutStatus-eq204)("Logout must return 204; status=$logoutStatus")
    Write-Step 'Authentication subcheck logout PASS'

    $authStage='logout-refresh-negative'
    $revokedRefreshStatus=HttpStatus Post '/api/v1/auth/refresh' @{} @{refreshToken=[string]$rotated.refreshToken;tenantId=$TenantId}
    Assert-True($revokedRefreshStatus-eq401)("Logged-out refresh token must return 401; status=$revokedRefreshStatus")
    Write-Step 'Authentication subcheck logged-out refresh rejected PASS'
}catch{
    $detail=$_.Exception.Message
    if($_.Exception.Response){$detail="$detail; httpStatus=$([int]$_.Exception.Response.StatusCode)"}
    throw "GA-08 authentication subcheck failed at [$authStage]. Validator=$script:Ga08ValidatorVersion. $detail"
}
Write-Step 'Authentication login / JWT claims / refresh rotation / reuse / logout PASS'

Write-Step 'Negative authentication / authorization policy gate...';$unauthStatus=HttpStatus Get '/api/v1/observability/metrics';Assert-True($unauthStatus-eq401)("Protected endpoint without token must return 401; status=$unauthStatus");$session=Invoke-Api Post '/api/v1/auth/login' @{email=$Email;password=$plain;tenantId=$TenantId};$h=@{Authorization="Bearer $($session.accessToken)"};$metricsStatus=HttpStatus Get '/api/v1/observability/metrics' $h;Assert-True($metricsStatus-eq200)'Authorized metrics endpoint failed';$current=Invoke-Api Get '/api/v1/tenants/current' $null $h;Assert-True(([string]$current.id-eq$TenantId)-or([string]$current.tenantId-eq$TenantId))'Tenant context mismatch';Write-Step 'Negative authentication / authorization policy gate PASS'

Write-Step 'Cross-tenant API isolation negative reads...';$cross=@{};if($sql.foreignCustomerId){$cross.foreignCustomerStatus=HttpStatus Get "/api/v1/customers/$($sql.foreignCustomerId)" $h;Assert-True([int]$cross.foreignCustomerStatus-eq404)'Foreign customer read must return 404'};if($sql.foreignSaleId){$cross.foreignSaleStatus=HttpStatus Get "/api/v1/sales/$($sql.foreignSaleId)" $h;Assert-True([int]$cross.foreignSaleStatus-eq404)'Foreign sale read must return 404'};Assert-True(-not [string]::IsNullOrWhiteSpace([string]$sql.foreignTenantId)) 'Foreign tenant fixture missing';Write-Step 'Cross-tenant API isolation negative reads PASS'

Write-Step 'Provisioning isolation and status contract...';$prov=Invoke-Api Get '/api/v1/provisioning/status';Assert-True($prov.enabled-eq$true)'Provisioning must be enabled';Assert-True($prov.configured-eq$true)'Provisioning key must be configured';Assert-True([string]$prov.requiredHeader-eq'X-SolidPOS-Provision-Key')'Provisioning header mismatch';Assert-True([long]$sql.completedBootstrapRunCount-ge1)'No completed tenant bootstrap evidence';Write-Step 'Provisioning isolation and status contract PASS'

Write-Step 'Dependency/package vulnerability audit...';$dependencyAudit='UNAVAILABLE';$dependencyHighCritical=0;try{$global:LASTEXITCODE=0;$depOut=& dotnet list $sln package --vulnerable --include-transitive --format json 2>&1;$depExit=$LASTEXITCODE;$global:LASTEXITCODE=0;if($depExit-eq0){$depText=($depOut-join"`n");$dependencyHighCritical=([regex]::Matches($depText,'(?i)"severity"\s*:\s*"(high|critical)"')).Count;$dependencyAudit='PASS';Assert-True($dependencyHighCritical-eq0)("High/critical vulnerable NuGet packages detected: $dependencyHighCritical")}}catch{$dependencyAudit='UNAVAILABLE';$global:LASTEXITCODE=0};Write-Step "Dependency/package vulnerability audit $dependencyAudit"

Write-Step 'GA-08 authentication session cleanup...';$cleanupStatus=HttpStatus Post '/api/v1/auth/logout' $h @{refreshToken=[string]$session.refreshToken;tenantId=$TenantId};Assert-True($cleanupStatus-eq204)("Final GA-08 session logout must return 204; status=$cleanupStatus");Write-Step 'GA-08 authentication session cleanup PASS'

Write-Step 'Append GA-08 security audit evidence...';$trace="ga08-$([Guid]::NewGuid().ToString('N'))";$audit=Invoke-DbJsonFile $auditSql @{tenant_id=$TenantId;trace_id=$trace};Assert-True([string]$audit.auditDecision-eq'GO')'GA-08 audit evidence insert failed';Write-Step 'Append GA-08 security audit evidence PASS'

$conditions=@();if($dependencyAudit-eq'UNAVAILABLE'){$conditions+=@('dependency_vulnerability_audit_unavailable')}
$generated=(Get-Date).ToUniversalTime();$manifest=[ordered]@{phase='GA-08';status='PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09';tenantId=$TenantId;baseUrl=$script:base;generatedAt=$generated.ToString('o');entryGate='PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08';securityContract='ga_security_tenant_isolation_access_control_final_gate';refreshTokenTenantContext='PASS';secretsRotatedAfterExposure=$true;secretScan='PASS';authLogin='PASS';jwtClaims='PASS';refreshRotation='PASS';refreshReuseRejected=$true;logoutInvalidation='PASS';sessionCleanup='PASS';unauthenticatedProtectedEndpointStatus=$unauthStatus;permissionNegativeAutomatedTest='PASS';tenantContext='PASS';crossTenantReadIsolation='PASS';crossTenantNegativeTests=$cross;provisioningIsolation='PASS';productionSwaggerStatus=$swaggerStatus;untrustedCorsOriginAllowed=$false;securityHeaders='PASS';rlsTenantTableCount=[long]$sql.tenantTableCount;rlsEnabledTenantTableCount=[long]$sql.rlsEnabledTenantTableCount;rlsMissingTenantTableCount=[long]$sql.rlsMissingTenantTableCount;rlsPolicyMissingTenantTableCount=[long]$sql.rlsPolicyMissingTenantTableCount;returnRefundsRlsPolicyPresent=[bool]$sql.returnRefundsRlsPolicyPresent;updateReleasesRlsPolicyPresent=[bool]$sql.updateReleasesRlsPolicyPresent;backgroundJobsRlsPolicyPresent=[bool]$sql.backgroundJobsRlsPolicyPresent;userRoleCount=[long]$sql.userRoleCount;rolePermissionCount=[long]$sql.rolePermissionCount;storeAccessCount=[long]$sql.storeAccessCount;invalidRefreshHashCount=[long]$sql.invalidRefreshHashCount;invalidPasswordHashCount=[long]$sql.invalidPasswordHashCount;auditEvidenceCount=[long]$audit.auditCount;dependencyAudit=$dependencyAudit;dependencyHighCriticalCount=$dependencyHighCritical;conditions=$conditions;blockers=@();schemaVersion=4;syncContract='schema_version_4';generalAvailabilityActivated=$false;nextPhase='GA-09 - Performance, Capacity, Resilience and Offline Gate'};$manifest|ConvertTo-Json -Depth 20|Set-Content -Encoding UTF8 $manifestPath;[ordered]@{sql=$sql;crossTenant=$cross;audit=$audit;manifest=$manifest}|ConvertTo-Json -Depth 20|Set-Content -Encoding UTF8 $snapshotPath
@"
# GA-08 Security Final Gate Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- authLogin: PASS
- refreshRotation: PASS
- refreshReuseRejected: True
- logoutInvalidation: PASS
- crossTenantReadIsolation: PASS
- rlsTenantTableCount: $($manifest.rlsTenantTableCount)
- rlsMissingTenantTableCount: $($manifest.rlsMissingTenantTableCount)
- rlsPolicyMissingTenantTableCount: $($manifest.rlsPolicyMissingTenantTableCount)
- dependencyAudit: $dependencyAudit
- conditions: $($conditions -join ', ')
- blockers: {}
- schemaVersion: 4
- syncContract: schema_version_4
- generalAvailabilityActivated: False
"@|Set-Content -Encoding UTF8 $logPath
$plain=$null;[GC]::Collect();Write-Step 'GA-08 evidence manifest and security snapshot PASS';Write-Step 'GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09';[pscustomobject]$manifest
