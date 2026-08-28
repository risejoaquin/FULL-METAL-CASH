# Public GA Activation Execution — Commands

Run only after the Public GA Activation Decision is formally reviewed as PASS / GO APPROVED.

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos-public-ga-activation-execution

Unblock-File .\scripts\ga\validate-public-ga-activation-execution.ps1
Unblock-File .\scripts\ga\validate-public-ga-activation-decision.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

$securePassword = Read-Host "Password admin@micafeteria.com" -AsSecureString
$env:DATABASE_URL = Read-Host "DATABASE_URL"

.\scripts\ga\validate-public-ga-activation-execution.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -DashboardUrl "https://cooperative-connection-production-4fea.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ExecuteActivation `
  -ConfirmationPhrase ACTIVATE_PUBLIC_GA `
  -AllowedWaitingConnectionCount 12 `
  -AllowedNegativeStockItemCount 0 `
  -PublicGaReadinessConcurrency 3 `
  -ConcurrencyProbeRequests 6 `
  -MaxReadinessP95Ms 1200 `
  -WpfVisualConfirmed `
  -SkipDashboardBuild `
  -SkipActivationDecisionRevalidation
```

The skip switch is valid only when the immediately preceding Public GA Activation Decision PASS logs have already been reviewed. Omitting it performs the decision validation again before activation.

Expected success:

`PASS PUBLIC GA ACTIVATION EXECUTION / PUBLIC GA ACTIVATED / POSTFLIGHT PASS`

## Manual rollback

```powershell
.\scripts\ga\rollback-public-ga-activation.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -DatabaseUrl $env:DATABASE_URL `
  -ConfirmationPhrase ROLLBACK_PUBLIC_GA `
  -Reason "operator_requested_rollback"
```
