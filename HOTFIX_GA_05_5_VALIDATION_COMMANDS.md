# HOTFIX GA-05.5 — Stable Channel Release Index Contract

## Root cause
GA-05 invokes `vpk pack --channel stable`. Velopack names the release feed using `releases.{channel}.json`, therefore the expected index is `releases.stable.json`. The previous validator incorrectly required `releases.win.json`, which is the default Windows channel index and does not match the explicit `stable` channel.

## Fix
- Require exactly `releases.stable.json` for the explicit stable candidate channel.
- Parse the index as JSON.
- Require at least one asset for the requested RC version.
- Require exactly one `Full` asset for the RC.
- Require the referenced full `.nupkg` to exist in Velopack output.
- Preserve the release index and generated `.nupkg` files in the GA-05 artifact evidence directory.
- No stable release is posted or activated in production.

## Revalidation
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-05-stable-release-candidate-build-signing-provenance.ps1
Unblock-File .\scripts\ga\validate-ga-04-production-data-integrity-financial-reconciliation.ps1
Unblock-File .\scripts\ga\validate-ga-03-support-incident-slo-operations-readiness.ps1
Unblock-File .\scripts\ga\validate-ga-02-sync-queue-sla-closure.ps1
Unblock-File .\scripts\ga\validate-ga-01-general-availability-baseline-freeze.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-05-stable-release-candidate-build-signing-provenance.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -ReleaseVersion "1.0.0-rc.1" `
  -SkipDashboardBuild
```

Expected terminal gate only if all checks pass:
`[GA-05] GA-05 PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06`
