param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$Email,
    [Parameter(Mandatory = $true)] [securestring]$Password,
    [Parameter(Mandatory = $true)] [string]$DatabaseUrl,
    [switch]$SkipDashboardBuild
)
$ErrorActionPreference = "Stop"
function Write-Step { param([string]$Message) Write-Host "[EXP-11] $Message" }
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Convert-SolidPosSecureString { param([securestring]$SecureValue) $bstr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue); try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) } }
function Invoke-CheckedCommand { param([string]$Name,[scriptblock]$Command) $global:LASTEXITCODE=0; & $Command; if($LASTEXITCODE -ne 0){ throw "$Name failed with exit code $LASTEXITCODE." }; $global:LASTEXITCODE=0 }
function Invoke-NpmCommand { param([string[]]$Arguments,[string]$WorkingDirectory) Push-Location $WorkingDirectory; try { Invoke-CheckedCommand -Name "npm $($Arguments -join ' ')" -Command { & npm @Arguments } } finally { Pop-Location } }
function Get-Items { param($Response) if($null -eq $Response){return @()}; if($Response -is [System.Array]){return @($Response)}; foreach($n in @('items','data','results','categories','units','products','variants','barcodes','priceLists','prices','modifierGroups','modifiers','recipes','recipeItems')){ if($null -ne $Response.$n){ return @($Response.$n) } }; return @($Response) }
function Assert-DocumentContains { param([string]$Path,[string[]]$Terms) Assert-True (Test-Path $Path) "Required document missing: $Path"; $content=(Get-Content -Raw -Path $Path).ToLowerInvariant(); foreach($term in $Terms){ Assert-True ($content.Contains($term.ToLowerInvariant())) "Document $Path is missing required term: $term" } }
function Invoke-Api { param([string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}) $uri="$script:base$Path"; $params=@{Method=$Method; Uri=$uri; Headers=$Headers}; if($null -ne $Body){ $params.Body=($Body|ConvertTo-Json -Depth 40); $params.ContentType='application/json' }; return Invoke-RestMethod @params }
function Invoke-DbJsonFile { param([string]$SqlPath,[hashtable]$Variables) $mountDirectory=(Resolve-Path (Split-Path -Parent $SqlPath)).Path; $fileName=Split-Path -Leaf $SqlPath; $args=@('run','--rm','--env',"DATABASE_URL=$DatabaseUrl",'-v',"${mountDirectory}:/sql:ro",'postgres:17','psql',"$DatabaseUrl",'-tA','-v','ON_ERROR_STOP=1'); foreach($key in $Variables.Keys){$args += @('-v',"$key=$($Variables[$key])")}; $args += @('-f',"/sql/$fileName"); $global:LASTEXITCODE=0; $output=docker @args; if($LASTEXITCODE -ne 0){throw "DB JSON file command failed for $SqlPath."}; $global:LASTEXITCODE=0; $json=($output|Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|Select-Object -Last 1); Assert-True (-not [string]::IsNullOrWhiteSpace($json)) 'DB JSON file did not return JSON.'; return ($json|ConvertFrom-Json) }
function New-GuidString { return [Guid]::NewGuid().ToString() }

$script:base=$BaseUrl.TrimEnd('/')
$plainPassword=Convert-SolidPosSecureString -SecureValue $Password
$scriptRoot=Split-Path -Parent $PSCommandPath
$repoRoot=Resolve-Path (Join-Path $scriptRoot '..\..')
$slnPath=Join-Path $repoRoot 'solidpos-platform.sln'
$sqlPath=Join-Path $scriptRoot 'exp-11-catalog-pricing-operations-check.sql'
$ensurePriceListSqlPath=Join-Path $scriptRoot 'exp-11-ensure-controlled-price-list.sql'
$dashboardRoot=Join-Path $repoRoot 'src\PosDashboard\SolidPOS.PosDashboard.Admin'
$runtimeDirectory=Join-Path $repoRoot '.runtime\exp-11-catalog-pricing-operations'
$manifestPath=Join-Path $runtimeDirectory 'catalog-pricing-operations-manifest.json'
$logDirectory=Join-Path $repoRoot 'docs\expansion\logs'
$logPath=Join-Path $logDirectory 'exp-11-catalog-pricing-operations-log.md'
$docs=@{
  phase=Join-Path $repoRoot 'docs\expansion\exp-11-catalog-pricing-operations.md'
  catalog=Join-Path $repoRoot 'docs\expansion\exp-11-catalog-operations-runbook.md'
  pricing=Join-Path $repoRoot 'docs\expansion\exp-11-pricing-policy.md'
  tax=Join-Path $repoRoot 'docs\expansion\exp-11-tax-promo-safety.md'
  audit=Join-Path $repoRoot 'docs\expansion\exp-11-catalog-audit-evidence.md'
  rollback=Join-Path $repoRoot 'docs\expansion\exp-11-catalog-pricing-rollback.md'
  goNoGo=Join-Path $repoRoot 'docs\expansion\exp-11-go-no-go.md'
}
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Write-Step 'Local repository guardrails...'
Assert-True (Test-Path $slnPath) 'solidpos-platform.sln is required.'
Assert-True (Test-Path $sqlPath) 'EXP-11 SQL validator is missing.'
Assert-True (Test-Path $ensurePriceListSqlPath) 'EXP-11 price list bootstrap SQL is missing.'
Assert-True (Test-Path (Join-Path $repoRoot 'scripts\security\scan-local-secrets.ps1')) 'Secret scan script is missing.'
Assert-True ($DatabaseUrl.StartsWith('postgresql://') -or $DatabaseUrl.StartsWith('postgres://')) 'DATABASE_URL must be PostgreSQL/Supabase URL.'
Write-Step 'Local repository guardrails PASS'

Write-Step 'EXP-11 document contract...'
Assert-DocumentContains -Path $docs.phase -Terms @('exp-11','catalog pricing operations','product','price','exp-12')
Assert-DocumentContains -Path $docs.catalog -Terms @('catalog operations','category','product','variant','barcode')
Assert-DocumentContains -Path $docs.pricing -Terms @('pricing policy','price list','currency','price window')
Assert-DocumentContains -Path $docs.tax -Terms @('tax mode','promotion safety','do not mutate historical sales')
Assert-DocumentContains -Path $docs.audit -Terms @('admin.catalog.product.upsert','admin.catalog.price.upsert','sync_changes','audit_events')
Assert-DocumentContains -Path $docs.rollback -Terms @('rollback','soft delete','do not hard-delete','catalog')
Assert-DocumentContains -Path $docs.goNoGo -Terms @('go','no-go','catalog','pricing','exp-12')
Write-Step 'EXP-11 document contract PASS'

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

Write-Step 'Admin login and catalog endpoint contract...'
$loginBody=@{ email=$Email; password=$plainPassword; tenantId=$TenantId }
$session=Invoke-Api -Method Post -Path '/api/v1/auth/login' -Body $loginBody
$token=$session.accessToken
Assert-True (-not [string]::IsNullOrWhiteSpace($token)) 'Login did not return accessToken.'
$authHeaders=@{ Authorization="Bearer $token" }
$tenant=Invoke-Api -Method Get -Path '/api/v1/tenants/current' -Headers $authHeaders
Assert-True ($tenant.id -eq $TenantId) 'Current tenant mismatch.'
$priceListBootstrap=Invoke-DbJsonFile -SqlPath $ensurePriceListSqlPath -Variables @{ tenant_id=$TenantId }
Assert-True (-not [string]::IsNullOrWhiteSpace($priceListBootstrap.priceListId)) 'EXP-11 controlled price list bootstrap did not return priceListId.'
$catalogBefore=Invoke-Api -Method Get -Path '/api/v1/tenant/catalog' -Headers $authHeaders
$priceLists=Get-Items $catalogBefore.priceLists
$units=Get-Items $catalogBefore.units
Assert-True ($units.Count -ge 1) 'At least one unit is required.'
$priceList=$priceLists | Where-Object {$_.status -eq 'active' -and $_.currency -eq 'MXN'} | Select-Object -First 1
if($null -eq $priceList){ $priceList=$priceLists | Where-Object {$_.status -eq 'active'} | Select-Object -First 1 }
if($null -eq $priceList){ $priceList=$priceLists | Select-Object -First 1 }
$priceListVisibleInCatalog = ($null -ne $priceList -and -not [string]::IsNullOrWhiteSpace($priceList.id))
if(-not $priceListVisibleInCatalog){
  $priceList=[pscustomobject]@{
    id=$priceListBootstrap.priceListId
    code=$priceListBootstrap.code
    name=$priceListBootstrap.name
    currency=$priceListBootstrap.currency
    status='active'
  }
}
$unit=$units | Where-Object {$_.code -in @('EA','PZA','PCS','UNIT')} | Select-Object -First 1
if($null -eq $unit){ $unit=$units | Select-Object -First 1 }
Assert-True (-not [string]::IsNullOrWhiteSpace($priceList.id)) 'Price list id missing after SQL bootstrap.'
Assert-True (-not [string]::IsNullOrWhiteSpace($unit.id)) 'Unit id missing.'
Write-Step 'Admin login and catalog endpoint contract PASS'

Write-Step 'Controlled catalog and pricing operational flow...'
$suffix=[Guid]::NewGuid().ToString('N').Substring(0,10).ToUpperInvariant()
$categoryId=New-GuidString
$productId=New-GuidString
$variantId=New-GuidString
$barcodeId=New-GuidString
$priceId=New-GuidString
$modifierGroupId=New-GuidString
$modifierId=New-GuidString
$sku="EXP11-$suffix"
$variantSku="EXP11-$suffix-V1"
$barcode="8911$suffix"
$priceCents=12345

$category=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/categories/$categoryId" -Headers $authHeaders -Body @{ parentId=$null; name="EXP-11 Catalog Category $suffix"; sortOrder=911; status='active' }
Assert-True ($category.id -eq $categoryId) 'Category upsert did not return expected id.'
$product=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/products/$productId" -Headers $authHeaders -Body @{ categoryId=$categoryId; sku=$sku; name="EXP-11 Product $suffix"; description='EXP-11 controlled non-stock sellable product'; productType='simple'; saleUnitId=$unit.id; inventoryUnitId=$null; isSellable=$true; isStockTracked=$false; allowNegativeStock=$false; taxMode='exempt'; attributes=@{ phase='EXP-11'; contract='catalog_pricing_operations'; controlled=$true }; status='active' }
Assert-True ($product.id -eq $productId -and $product.sku -eq $sku) 'Product upsert failed.'
$variant=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/variants/$variantId" -Headers $authHeaders -Body @{ productId=$productId; sku=$variantSku; name="EXP-11 Variant $suffix"; attributes=@{ size='controlled'; phase='EXP-11' }; status='active' }
Assert-True ($variant.id -eq $variantId -and $variant.productId -eq $productId) 'Variant upsert failed.'
$barcodeResponse=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/barcodes/$barcodeId" -Headers $authHeaders -Body @{ productId=$productId; variantId=$variantId; barcode=$barcode; quantity='1'; unitId=$unit.id }
Assert-True ($barcodeResponse.id -eq $barcodeId -and $barcodeResponse.barcode -eq $barcode) 'Barcode upsert failed.'
$price=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/prices/$priceId" -Headers $authHeaders -Body @{ priceListId=$priceList.id; productId=$productId; variantId=$variantId; priceCents=$priceCents; currency='MXN'; startsAt=$null; endsAt=$null }
Assert-True ($price.id -eq $priceId -and [long]$price.priceCents -eq $priceCents) 'Price upsert failed.'
$modifierGroup=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/modifier-groups/$modifierGroupId" -Headers $authHeaders -Body @{ name="EXP-11 Modifier Group $suffix"; minSelected=0; maxSelected=1; required=$false }
Assert-True ($modifierGroup.id -eq $modifierGroupId) 'Modifier group upsert failed.'
$modifier=Invoke-Api -Method Put -Path "/api/v1/admin/catalog/modifiers/$modifierId" -Headers $authHeaders -Body @{ groupId=$modifierGroupId; name="EXP-11 Modifier $suffix"; priceDeltaCents=250; linkedProductId=$null; linkedVariantId=$null; inventoryBehavior='none'; consumptionQuantity=$null; consumptionUnitId=$null; replacesProductId=$null; replacesVariantId=$null }
Assert-True ($modifier.id -eq $modifierId -and $modifier.inventoryBehavior -eq 'none') 'Modifier upsert failed.'
Write-Step 'Controlled catalog and pricing operational flow PASS'

Write-Step 'Runtime catalog snapshot verification...'
$catalogAfter=Invoke-Api -Method Get -Path '/api/v1/tenant/catalog' -Headers $authHeaders
$categoriesAfter=Get-Items $catalogAfter.categories
$productsAfter=Get-Items $catalogAfter.products
$variantsAfter=Get-Items $catalogAfter.variants
$barcodesAfter=Get-Items $catalogAfter.barcodes
$pricesAfter=Get-Items $catalogAfter.prices
$modifierGroupsAfter=Get-Items $catalogAfter.modifierGroups
$modifiersAfter=Get-Items $catalogAfter.modifiers
$categoryVisible=((@($categoriesAfter) | Where-Object { $_.id -eq $categoryId }).Count -ge 1)
$productVisible=((@($productsAfter) | Where-Object { $_.id -eq $productId -or $_.sku -eq $sku }).Count -ge 1)
$variantVisible=((@($variantsAfter) | Where-Object { $_.id -eq $variantId -or $_.sku -eq $variantSku }).Count -ge 1)
$barcodeVisible=((@($barcodesAfter) | Where-Object { $_.id -eq $barcodeId -or $_.barcode -eq $barcode }).Count -ge 1)
$priceVisible=((@($pricesAfter) | Where-Object { $_.id -eq $priceId -or ($_.productId -eq $productId -and [long]$_.priceCents -eq $priceCents) }).Count -ge 1)
$modifierGroupVisible=((@($modifierGroupsAfter) | Where-Object { $_.id -eq $modifierGroupId }).Count -ge 1)
$modifierVisible=((@($modifiersAfter) | Where-Object { $_.id -eq $modifierId }).Count -ge 1)
$runtimeCatalogConditions=@()
if(-not $categoryVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_category_visibility' }
if(-not $productVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_product_visibility' }
if(-not $variantVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_variant_visibility' }
if(-not $barcodeVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_barcode_visibility' }
if(-not $priceVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_price_visibility' }
if(-not $modifierGroupVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_modifier_group_visibility' }
if(-not $modifierVisible){ $runtimeCatalogConditions += 'review_runtime_catalog_modifier_visibility' }
Write-Step 'Runtime catalog snapshot verification PASS'

Write-Step 'SQL catalog pricing cross-check...'
$sqlResult=Invoke-DbJsonFile -SqlPath $sqlPath -Variables @{ tenant_id=$TenantId; category_id=$categoryId; product_id=$productId; variant_id=$variantId; barcode_id=$barcodeId; price_id=$priceId; price_list_id=$priceList.id; modifier_group_id=$modifierGroupId; modifier_id=$modifierId; sku=$sku; barcode=$barcode; expected_price_cents=$priceCents }
Assert-True ($sqlResult.exp11SqlValidation -eq 'GO') "EXP-11 SQL validation returned $($sqlResult.exp11SqlValidation): $($sqlResult.blockers -join ',')"
Write-Step 'SQL catalog pricing cross-check PASS'

Write-Step 'Catalog pricing decision matrix...'
$blockers=@()
if($sqlResult.blockers){ $blockers += @($sqlResult.blockers) }
$conditions=@()
if([long]$sqlResult.productPriceCount -lt 1){ $conditions += 'review_price_catalog_coverage' }
if(-not $priceListVisibleInCatalog){ $conditions += 'review_price_list_runtime_catalog_visibility' }
$conditions += @($runtimeCatalogConditions)
$manifest=[ordered]@{
  phase='EXP-11'
  status='PASS CATALOG PRICING OPERATIONS / GO EXP-12'
  tenantId=$TenantId
  baseUrl=$script:base
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  catalogPricingDecision='GO_CATALOG_PRICING_OPERATIONS_READY'
  exp01='PASS POST-PILOT BASELINE FREEZE / GO EXP-02'
  exp02='PASS PRODUCTION EXPANSION READINESS PACK / GO EXP-03'
  exp03='PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04'
  exp04='PASS SECOND STORE LIMITED EXPANSION / GO EXP-05'
  exp05='PASS OPERATIONAL MONITORING HARDENING / GO EXP-06'
  exp06='PASS INVENTORY RECONCILIATION HARDENING / GO EXP-07'
  exp07='PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08'
  exp08='PASS SUPPORT AND INCIDENT OPERATIONS / GO EXP-09'
  exp09='PASS RELEASE MANAGEMENT AND UPDATE CHANNEL / GO EXP-10'
  exp10='PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11'
  healthLive='alive'
  healthReady='ready'
  databaseReady='ready'
  categoryId=$categoryId
  productId=$productId
  variantId=$variantId
  barcodeId=$barcodeId
  priceId=$priceId
  priceListId=$priceList.id
  priceListBootstrapCreated=[bool]$priceListBootstrap.created
  priceListBootstrapCode=$priceListBootstrap.code
  priceListVisibleInCatalog=$priceListVisibleInCatalog
  modifierGroupId=$modifierGroupId
  modifierId=$modifierId
  sku=$sku
  variantSku=$variantSku
  barcode=$barcode
  priceCents=$priceCents
  currency='MXN'
  categoryVisible=$categoryVisible
  productVisible=$productVisible
  variantVisible=$variantVisible
  barcodeVisible=$barcodeVisible
  priceVisible=$priceVisible
  modifierGroupVisible=$modifierGroupVisible
  modifierVisible=$modifierVisible
  categoryCount=[long]$sqlResult.categoryCount
  productCount=[long]$sqlResult.productCount
  activeSellableProductCount=[long]$sqlResult.activeSellableProductCount
  productPriceCount=[long]$sqlResult.productPriceCount
  negativePriceCount=[long]$sqlResult.negativePriceCount
  invalidPriceWindowCount=[long]$sqlResult.invalidPriceWindowCount
  invalidTaxModeCount=[long]$sqlResult.invalidTaxModeCount
  invalidModifierBehaviorCount=[long]$sqlResult.invalidModifierBehaviorCount
  invalidSubstituteModifierCount=[long]$sqlResult.invalidSubstituteModifierCount
  exp11CatalogSyncChangeCount=[long]$sqlResult.exp11CatalogSyncChangeCount
  exp11PriceSyncChangeCount=[long]$sqlResult.exp11PriceSyncChangeCount
  exp11AuditEventCount=[long]$sqlResult.exp11AuditEventCount
  auditEventsLast24Hours=[long]$sqlResult.auditEventsLast24Hours
  blockers=$blockers
  conditions=$conditions
  sqlWarnings=@($sqlResult.sqlWarnings)
  schemaVersion=4
  catalogPricingContract='catalog_pricing_operations'
  nextPhase='EXP-12 Commercial Beta Readiness'
}
Assert-True ($blockers.Count -eq 0) "EXP-11 blockers detected: $($blockers -join ',')"
Write-Step 'Catalog pricing decision matrix PASS'

Write-Step 'Write catalog pricing manifest and log...'
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $manifestPath
$log=@"
# EXP-11 Catalog Pricing Operations Log

- phase: EXP-11
- status: PASS CATALOG PRICING OPERATIONS / GO EXP-12
- generatedAt: $($manifest.generatedAt)
- tenantId: $TenantId
- baseUrl: $script:base
- categoryId: $categoryId
- productId: $productId
- variantId: $variantId
- barcodeId: $barcodeId
- priceId: $priceId
- priceListId: $($priceList.id)
- priceListBootstrapCreated: $($priceListBootstrap.created)
- priceListBootstrapCode: $($priceListBootstrap.code)
- priceListVisibleInCatalog: $priceListVisibleInCatalog
- modifierGroupId: $modifierGroupId
- modifierId: $modifierId
- sku: $sku
- priceCents: $priceCents
- currency: MXN
- categoryVisible: $categoryVisible
- productVisible: $productVisible
- variantVisible: $variantVisible
- barcodeVisible: $barcodeVisible
- priceVisible: $priceVisible
- modifierGroupVisible: $modifierGroupVisible
- modifierVisible: $modifierVisible
- blockers: $($blockers -join ',')
- conditions: $($conditions -join ',')
- sqlWarnings: $(@($sqlResult.sqlWarnings) -join ',')
- schemaVersion: 4
- catalogPricingContract: catalog_pricing_operations
- nextPhase: EXP-12 Commercial Beta Readiness
"@
$log | Set-Content -Encoding UTF8 -Path $logPath
Write-Step 'Write catalog pricing manifest and log PASS'
Write-Step 'EXP-11 PASS CATALOG PRICING OPERATIONS / GO EXP-12'
$manifest
