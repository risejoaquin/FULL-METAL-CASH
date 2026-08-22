# HOTFIX GA-05.1 — win-x64 RID restore contract

## Problema corregido
GA-05 llegaba a `dotnet publish -r win-x64 --no-restore`, pero el restore anterior de la solución no había materializado el target `net8.0-windows/win-x64` en `project.assets.json`, produciendo `NETSDK1047`.

## Corrección
- `SolidPOS.PosCore.Wpf.csproj` declara `RuntimeIdentifiers=win-x64`.
- El validator ejecuta un restore explícito del grafo WPF para `win-x64` antes de los dos publishes reproducibles.
- Ambos publishes mantienen exactamente el mismo RID/configuración y continúan usando `--no-restore`.
- No se modifica producción ni se promueve el canal `stable`.

## Validación
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

Resultado requerido:
`[GA-05] GA-05 PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06`
