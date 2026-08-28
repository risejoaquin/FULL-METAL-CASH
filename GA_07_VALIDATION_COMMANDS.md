# GA-07 Validation Commands

## Precondiciones

- Repo completo GA-07.
- `$securePassword` cargado como `SecureString`.
- `$env:DATABASE_URL` apuntando a la DB productiva correcta.
- Docker Desktop disponible.
- PosServer productivo desplegado con GA-06.9+.

## Validación

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

Unblock-File .\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## No ejecutar

```powershell
-ResetSchema
```

GA-07 no tiene migraciones nuevas y no requiere redeploy adicional.

## Resultado final requerido

```text
[GA-07] GA-07 PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08
```

Si falla, enviar el log completo desde `[GA-07]` hasta la excepción y no avanzar a GA-08.
