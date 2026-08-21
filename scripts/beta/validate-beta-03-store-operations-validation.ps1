param(
    [Parameter(Mandatory=$true)][string]$BaseUrl,
    [Parameter(Mandatory=$true)][string]$TenantId,
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][securestring]$Password,
    [Parameter(Mandatory=$true)][string]$DatabaseUrl,
    [string]$StoreCode='MAIN',
    [string]$ProductSku='QSR-AMERICANO',
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference='Stop'
function Write-Step([string]$m){Write-Host "[BETA-03] $m"}
function Assert-True([bool]$c,[string]$m){if(-not$c){throw$m}}
function Convert-Secret([securestring]$s){$b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s);try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b)}}
function Invoke-Checked([string]$n,[scriptblock]$c){$global:LASTEXITCODE=0;&$c;if($LASTEXITCODE-ne0){throw "$n failed with exit code $LASTEXITCODE"};$global:LASTEXITCODE=0}
function Invoke-DbJson([string]$SqlPath,[hashtable]$Vars){$d=(Resolve-Path(Split-Path -Parent $SqlPath)).Path;$f=Split-Path -Leaf $SqlPath;$a=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${d}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1');foreach($k in $Vars.Keys){$a+=@('-v',"$k=$($Vars[$k])")};$a+=@('-f',"/sql/$f");$global:LASTEXITCODE=0;$o=docker @a;if($LASTEXITCODE-ne0){throw 'DB SQL validator failed.'};($o|Where-Object{$_}|Select-Object -Last 1)|ConvertFrom-Json}
$script:base=$BaseUrl.TrimEnd('/');$plain=Convert-Secret $Password;$scriptRoot=Split-Path -Parent $PSCommandPath;$repo=Resolve-Path(Join-Path $scriptRoot '..\..');$sln=Join-Path $repo 'solidpos-platform.sln';$pilot=Join-Path $repo 'scripts\pilot\validate-cash-drawer-shift-operations.ps1';$sqlPath=Join-Path $scriptRoot 'beta-03-store-operations-validation-check.sql';$runtime=Join-Path $repo '.runtime\beta-03-store-operations-validation';$manifestPath=Join-Path $runtime 'beta-03-store-operations-manifest.json';$logPath=Join-Path $repo 'docs\beta\logs\beta-03-store-operations-validation-log.md';New-Item -ItemType Directory -Force $runtime,(Split-Path $logPath)|Out-Null
Write-Step 'Repository guardrails...';Assert-True (Test-Path $sln) 'solution missing';Assert-True (Test-Path $pilot) 'PILOT-03 operational validator missing';Assert-True (Test-Path $sqlPath) 'BETA-03 SQL validator missing';Assert-True ($DatabaseUrl -match '^postgres(ql)?://') 'DATABASE_URL must be PostgreSQL.';Write-Step 'Repository guardrails PASS'
Write-Step 'Local secret scan...';Invoke-Checked 'secret scan' {& (Join-Path $repo 'scripts\security\scan-local-secrets.ps1') -Root $repo};Write-Step 'Local secret scan PASS'
Write-Step 'dotnet restore...';Invoke-Checked 'dotnet restore' {dotnet restore $sln};Write-Step 'dotnet restore PASS'
Write-Step 'dotnet build...';Invoke-Checked 'dotnet build' {dotnet build $sln --no-restore};Write-Step 'dotnet build PASS'
Write-Step 'dotnet test...';Invoke-Checked 'dotnet test' {dotnet test $sln --no-build};Write-Step 'dotnet test PASS'
Write-Step 'Production health and admin login...';$live=Invoke-RestMethod -Method Get -Uri "$script:base/health/live" -TimeoutSec 30;$ready=Invoke-RestMethod -Method Get -Uri "$script:base/health/ready" -TimeoutSec 30;Assert-True ($live.status -eq 'alive') 'liveness failed';Assert-True ($ready.status -eq 'ready') 'readiness failed';$session=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/auth/login" -ContentType 'application/json' -Body(@{email=$Email;password=$plain;tenantId=$TenantId}|ConvertTo-Json) -TimeoutSec 30;Assert-True (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'admin login missing accessToken';$adminHeaders=@{Authorization="Bearer $($session.accessToken)"};Write-Step 'Production health and admin login PASS'
Write-Step 'Executing controlled store operations flow...';$pilotArgs=@{BaseUrl=$script:base;TenantId=$TenantId;Email=$Email;Password=$Password;DatabaseUrl=$DatabaseUrl;StoreCode=$StoreCode;ProductSku=$ProductSku};if($SkipDashboardBuild){$pilotArgs.SkipDashboardValidation=$true};$op=& $pilot @pilotArgs | Select-Object -Last 1;Assert-True ($op.goNoGo -eq 'GO') 'Underlying cash drawer/shift flow did not return GO.';Assert-True ([long]$op.differenceCents -eq 0) 'Cash difference must be zero.';Assert-True ($op.shiftStatus -eq 'closed') 'Shift must be closed.';Write-Step 'Executing controlled store operations flow PASS'
Write-Step 'Issuing and reading digital receipt for controlled sale...';$receipt=Invoke-RestMethod -Method Post -Uri "$script:base/api/v1/receipts/$($op.sale1Id)/issue" -Headers $adminHeaders -ContentType 'application/json' -Body '{}' -TimeoutSec 30;Assert-True (-not [string]::IsNullOrWhiteSpace($receipt.id)) 'receipt id missing';Assert-True ($receipt.status -eq 'active') 'receipt not active';$protected=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/$($op.sale1Id)/digital" -Headers $adminHeaders -TimeoutSec 30;Assert-True ([string]$protected.id -eq [string]$receipt.id) 'protected receipt mismatch';Assert-True (-not [string]::IsNullOrWhiteSpace($receipt.publicToken)) 'public token missing';$public=Invoke-RestMethod -Method Get -Uri "$script:base/api/v1/receipts/public/$($receipt.publicToken)" -TimeoutSec 30;Assert-True ([string]$public.id -eq [string]$receipt.id) 'public receipt mismatch';Write-Step 'Issuing and reading digital receipt for controlled sale PASS'
Write-Step 'SQL source-of-truth reconciliation...';$sql=Invoke-DbJson $sqlPath @{tenant_id=$TenantId;shift_id=$op.shiftId;sale_id=$op.sale1Id;receipt_id=$receipt.id;expected_cash_cents=$op.expectedCashCents;counted_cash_cents=$op.countedCashCents;difference_cents=$op.differenceCents};Assert-True (@($sql.blockers).Count -eq 0) "BETA-03 SQL blockers: $(@($sql.blockers) -join ', ')";Assert-True ($sql.decision -eq 'GO') 'BETA-03 SQL decision was not GO.';Write-Step 'SQL source-of-truth reconciliation PASS'
$manifest=[ordered]@{phase='BETA-03';status='PASS BETA STORE OPERATIONS VALIDATION / GO BETA-04';tenantId=$TenantId;baseUrl=$script:base;generatedAt=(Get-Date).ToUniversalTime().ToString('o');betaDecision='GO_BETA_04';storeCode=$StoreCode;terminalId=$op.terminalId;shiftId=$op.shiftId;openingAmountCents=[long]$op.openingAmountCents;cashInCents=[long]$op.cashInCents;cashOutCents=[long]$op.cashOutCents;noSaleDrawerOpenCount=[long]$op.noSaleDrawerOpenCount;salesCount=[long]$op.salesCount;cashSalesCents=[long]$op.cashSalesCents;expectedCashCents=[long]$op.expectedCashCents;countedCashCents=[long]$op.countedCashCents;differenceCents=[long]$op.differenceCents;shiftStatus=$op.shiftStatus;saleId=$op.sale1Id;receiptId=$receipt.id;receiptNumber=$receipt.receiptNumber;receiptStatus=$receipt.status;sqlDecision=$sql.decision;blockers=@();conditions=@();schemaVersion=4;syncContract='schema_version_4';nextPhase='BETA-04 - Beta Offline Reliability Field Run'};$manifest|ConvertTo-Json -Depth 20|Set-Content -Encoding UTF8 $manifestPath
@"
# BETA-03 Store Operations Validation Log

- status: $($manifest.status)
- generatedAt: $($manifest.generatedAt)
- shiftId: $($manifest.shiftId)
- salesCount: $($manifest.salesCount)
- expectedCashCents: $($manifest.expectedCashCents)
- countedCashCents: $($manifest.countedCashCents)
- differenceCents: $($manifest.differenceCents)
- receiptId: $($manifest.receiptId)
- blockers: {}
- schemaVersion: 4
- syncContract: schema_version_4
"@|Set-Content -Encoding UTF8 $logPath
Write-Step 'BETA-03 evidence manifest PASS';Write-Step 'BETA-03 PASS BETA STORE OPERATIONS VALIDATION / GO BETA-04';$manifest
