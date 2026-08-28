# SolidPOS PILOT-08 Validation Commands - Backup / Restore / Rollback Drill

## Estado

PILOT-08 = PENDING USER VALIDATION

## Preparacion

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)
```

Debe responder:

```text
postgresql://
```

## Validacion principal

```powershell
Unblock-File .\scripts\pilot\validate-backup-restore-rollback-drill.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\pilot\validate-backup-restore-rollback-drill.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

## Resultado esperado

```text
[PILOT-08] PILOT-08 PASS REAL PRODUCTION / GO
```

## Artefactos esperados

```text
.runtime\pilot-08-backup-restore-rollback-drill\backups\pos-schema-backup.sql
.runtime\pilot-08-backup-restore-rollback-drill\backups\pilot-08-backup-manifest.json
docs\pilot\logs\pilot-08-backup-restore-rollback-drill-log.md
```

## Logs si falla

Enviar:

```text
Salida completa PowerShell desde el primer [PILOT-08]
docs/pilot/logs/pilot-08-backup-restore-rollback-drill-log.md si existe
.runtime/pilot-08-backup-restore-rollback-drill/backups/pilot-08-backup-manifest.json si existe
```


## HOTFIX 08.2

Restore aislado ahora ejecuta bootstrap de extensiones antes de aplicar el dump schema-only:

```sql
CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public;
CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;
```

Motivo: el dump de `pos` puede referenciar `public.citext`; un contenedor PostgreSQL limpio no trae esa extension habilitada por default.


## Hotfixes

- HOTFIX 08.1: PostgreSQL 17 pg_dump/psql tooling.
- HOTFIX 08.2: restore citext/pgcrypto extensions before schema restore.
- HOTFIX 08.3: restore container psql uses explicit TCP host/port and PGPASSWORD via docker exec.
