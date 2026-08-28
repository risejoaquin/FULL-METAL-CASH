# SolidPOS Iteration 03 Hotfix 03.1 — Sync Changes Migration Alignment

## Estado

Hotfix correctivo para `database/postgresql/018_sync_e2e_contract_hardening.sql`.

## Problema detectado

Durante la aplicación local de migraciones, la migración 018 falló con:

```text
ERROR: relation "sync_outbox_changes" does not exist
```

La Iteration 03 referenció incorrectamente una tabla llamada `sync_outbox_changes`, pero el contrato de pull existente usa la tabla runtime real:

```text
pos.sync_changes
```

## Corrección

Se reemplazó:

```sql
CREATE INDEX IF NOT EXISTS idx_sync_outbox_tenant_cursor
  ON sync_outbox_changes (tenant_id, id);
```

por:

```sql
CREATE INDEX IF NOT EXISTS idx_sync_changes_tenant_cursor
  ON sync_changes (tenant_id, changed_at, id);
```

## Decisión arquitectónica

No se introduce una tabla outbox nueva en esta iteración. Para mantener compatibilidad con el backend actual, el cursor/pull server-side sigue usando `pos.sync_changes`.

Cuando PosCore local se implemente, su outbox será local SQLite y se enviará al servidor vía `sync_inbox_events`; el pull remoto seguirá leyendo cambios desde `pos.sync_changes`.

## Validación esperada

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln

.\scripts\apply-postgresql-migrations.ps1
```

La migración 018 debe cerrar con:

```text
COMMIT
PostgreSQL migrations applied.
```

Después aplicar 018 en Supabase real y ejecutar smoke/sync E2E.
