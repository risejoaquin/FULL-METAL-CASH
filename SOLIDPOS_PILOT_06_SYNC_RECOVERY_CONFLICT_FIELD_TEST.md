# SolidPOS PILOT-06 - Sync Recovery / Conflict Field Test

## Estado

```text
SolidPOS PILOT-06 - Sync Recovery / Conflict Field Test = PENDING USER VALIDATION
```

## Objetivo

Validar en produccion, con datos controlados y scoped, los contratos reales de recuperacion de sync:

- evento `processing` atorado recuperado por lease vencido
- evento `dead_letter` listado por API
- retry manual de dead-letter por API
- reproceso de retry hacia `dead_letter` controlado
- conflicto real generado por `sale.voided` contra venta inexistente
- listado de conflictos pending por API
- resolucion del conflicto por API con `use_server`
- cambio de inbox `conflict` a `processed` despues de resolver
- audit trail `sync.conflict.resolved`
- SQL final sin hacks y scoped por IDs de PILOT-06
- contrato sync schema version 4
- GO/NO-GO

## Que se cambio

Se agregaron:

```text
scripts/pilot/validate-sync-recovery-conflict-field-test.ps1
scripts/pilot/pilot-06-sync-recovery-conflict-check.sql
PILOT_06_VALIDATION_COMMANDS.md
SOLIDPOS_PILOT_06_SYNC_RECOVERY_CONFLICT_FIELD_TEST.md
docs/pilot/pilot-06-operator-checklist.md
docs/pilot/pilot-06-go-no-go.md
docs/pilot/logs/pilot-06-sync-recovery-conflict-field-test-log.md
```

## Modulos afectados

```text
scripts/pilot
docs/pilot
```

No se modifica backend, PosCore, Dashboard ni migraciones. PILOT-06 usa contratos ya existentes.

## Decision tecnica

PILOT-06 no fuerza cambios funcionales. Valida los contratos reales:

```text
GET  /api/v1/sync/contract
GET  /api/v1/sync/bootstrap
POST /api/v1/sync/process
GET  /api/v1/sync/status
GET  /api/v1/sync/dead-letter
POST /api/v1/sync/dead-letter/{inboxEventId}/retry
POST /api/v1/sync/push
GET  /api/v1/sync/conflicts
POST /api/v1/sync/conflicts/{conflictId}/resolve
```

Para evitar falsos negativos, los datos controlados se crean con IDs propios de PILOT-06 y las aserciones SQL quedan scoped por:

```text
tenant_id
store_id
terminal_id
recovery_inbox_id
dead_letter_inbox_id
conflict_id
conflict_event_id
```

## Riesgos

- Requiere Docker local para `postgres:16 psql`.
- Requiere .NET 8 local para PosCore CLI.
- Deja un evento controlado en `dead_letter` como evidencia del retry/recovery path.
- Crea un conflicto controlado ya resuelto como evidencia de resolucion.
- Si Dashboard ya fue validado, puede usarse `-SkipDashboardValidation` para aislar sync.

## Resultado esperado

```text
[PILOT-06] PILOT-06 PASS REAL PRODUCTION / GO
```

## Logs si falla

Enviar:

```text
Salida completa PowerShell desde el primer [PILOT-06]
docs/pilot/logs/pilot-06-sync-recovery-conflict-field-test-log.md si existe
.runtime/pilot-06-sync-recovery-conflict-field-test.sqlite
.runtime/pilot-06-sync-recovery-conflict-field-test.sqlite-wal si existe
.runtime/pilot-06-sync-recovery-conflict-field-test.sqlite-shm si existe
```

## HOTFIX 06.1

Estado: PENDING USER VALIDATION

Correccion: `scripts/pilot/validate-sync-recovery-conflict-field-test.ps1` ahora pasa `--fingerprint` al comando PosCore CLI `bind`, usando el mismo fingerprint registrado en terminal enrollment. Tambien se redaccionan tokens en errores de CLI.
