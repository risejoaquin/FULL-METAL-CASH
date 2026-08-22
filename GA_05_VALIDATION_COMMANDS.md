# GA-05 Validation Commands

## 1. Unblock validators

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-05-stable-release-candidate-build-signing-provenance.ps1
Unblock-File .\scripts\ga\validate-ga-04-production-data-integrity-financial-reconciliation.ps1
Unblock-File .\scripts\ga\validate-ga-03-support-incident-slo-operations-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1
```

## 2. Default validation signing mode

This mode creates a short-lived `VALIDATION_SELF_SIGNED` certificate, verifies Authenticode, then removes it from the user certificate stores.

```powershell
.\scripts\ga\validate-ga-05-stable-release-candidate-build-signing-provenance.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ReleaseVersion "1.0.0-rc.1" `
  -SkipDashboardBuild
```

## 3. Production certificate mode

When a real code-signing certificate is available in `Cert:\CurrentUser\My`:

```powershell
.\scripts\ga\validate-ga-05-stable-release-candidate-build-signing-provenance.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ReleaseVersion "1.0.0-rc.1" `
  -SigningCertificateThumbprint "<THUMBPRINT>" `
  -SkipDashboardBuild
```

## Required PASS

```text
[GA-05] GA-05 PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06
```

Do not manually create a production `stable` release if the validator fails. GA-06 remains locked until this exact PASS is observed.
