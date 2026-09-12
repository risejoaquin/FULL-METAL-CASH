param(
    [switch]$StaticOnly
)

$ErrorActionPreference = 'Stop'

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Contains([string]$Path, [string[]]$Terms) {
    Assert-True (Test-Path $Path) "Required file missing: $Path"
    $content = Get-Content $Path -Raw
    foreach ($term in $Terms) {
        Assert-True ($content.Contains($term)) "Required contract term '$term' missing from $Path"
    }
}

$repo = (Resolve-Path (Join-Path (Split-Path -Parent $PSCommandPath) '..\..')).Path
$docPath = Join-Path $repo 'SOLIDPOS_V1_1_05_POSCORE_ERROR_UX_RECOVERY.md'
$manifestPath = Join-Path $repo 'V1_1_05_PACKAGE_MANIFEST.md'
$commandsPath = Join-Path $repo 'V1_1_05_VALIDATION_COMMANDS.md'
$modelsPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Recovery\OperatorRecoveryModels.cs'
$servicePath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Application\Recovery\OperatorRecoveryService.cs'
$salesViewModelPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\ViewModels\SalesViewModel.cs'
$xamlPath = Join-Path $repo 'src\PosCore\SolidPOS.PosCore.Wpf\MainWindow.xaml'
$serviceTestsPath = Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\OperatorRecoveryServiceTests.cs'
$wpfTestsPath = Join-Path $repo 'tests\SolidPOS.PosCore.UnitTests\SalesViewModelRecoveryTests.cs'
$workflowPath = Join-Path $repo '.github\workflows\solidpos-ci.yml'

Assert-Contains $docPath @(
    'Operator Recovery Contract',
    'Operator messages are concise',
    'Retries must reuse existing local operation identity',
    'ONLINE',
    'OFFLINE',
    'RECONNECTING',
    'Receipt printer failures',
    'Cash drawer failures'
)
Assert-Contains $manifestPath @('V1.1-05 PosCore Error UX & Recovery', '125d728067d9fd5357428c2111f21c88a76ab37a', 'schemaVersion: 4')
Assert-Contains $commandsPath @('validate-v1.1-05-poscore-error-ux-recovery.ps1', 'OperatorRecoveryServiceTests', 'SalesViewModelRecoveryTests')
Assert-Contains $modelsPath @('OperatorFailureKind', 'Retryable', 'NonRetryable', 'Offline', 'Hardware', 'Validation', 'AuthenticationAuthorization', 'Conflict', 'Unknown', 'OperatorRecoveryAction', 'PosCoreConnectivityState', 'TechnicalSummary')
Assert-Contains $servicePath @('OperatorRecoveryService', 'RetryKeepsOriginalSaleIdentity', 'RetryKeepsOriginalPaymentIdentity', 'ContinueOffline', 'RetryPrint', 'RetryDrawer')
Assert-Contains $salesViewModelPath @('OperatorErrorMessage', 'ConnectivityState', 'ShowPrinterFailure', 'ShowCashDrawerFailure', 'ContinueOfflineCommand', 'RetryPrintCommand', 'RetryDrawerCommand')
Assert-Contains $xamlPath @('Sales.ConnectivityState', 'Sales.OperatorErrorMessage', 'Sales.RetryPrintCommand', 'Sales.RetryDrawerCommand')
Assert-Contains $serviceTestsPath @('technical_exception_into_operator_message', 'retryable_offline_error', 'Retry_after_transient_sale_failure', 'Retry_after_payment_uncertainty')
Assert-Contains $wpfTestsPath @('Offline_state_is_visible', 'Online_recovery_updates', 'Printer_failure_exposes_retry_print', 'Cash_drawer_failure_exposes_retry_drawer')
Assert-Contains $workflowPath @('V1.1-05 PosCore error UX recovery static contract', 'validate-v1.1-05-poscore-error-ux-recovery.ps1 -StaticOnly')

Write-Host 'PASS V1.1-05 POSCORE ERROR UX RECOVERY STATIC CONTRACT'
if ($StaticOnly) { exit 0 }
