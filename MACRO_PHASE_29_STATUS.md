# Macro Fase 29 — Sync Conflict Resolution

Estado: IMPLEMENTED — pending local validation

## Objetivo

Cerrar la base offline-first de sincronización con:

- conflictos reales persistidos
- resolución manual
- resolución por estrategia aprobada
- reintentos controlados
- recuperación de eventos stuck en `processing`
- dead-letter después de intentos máximos
- bootstrap inicial para POS local

## Endpoints

```http
GET  /api/v1/sync/conflicts?status=&limit=
POST /api/v1/sync/conflicts/{conflictId}/resolve
GET  /api/v1/sync/bootstrap
```

## Decisión arquitectónica

El POS offline-first no debe mezclar "rechazo técnico" con "conflicto de negocio".

- `rejected/retry_pending`: evento técnico inválido, payload roto, tipo no soportado, error transitorio.
- `dead_letter`: evento técnico que agotó reintentos.
- `conflict`: evento válido que necesita decisión de negocio, por ejemplo una venta offline que cloud no puede aceptar por política de stock o estado divergente.

## Estrategias de resolución

```text
use_server
use_client
merge
compensate
```

`merge` y `compensate` requieren `resolvedPayload`.

## Persistencia

Migración nueva:

```text
database/postgresql/013_sync_conflict_resolution_runtime.sql
```

Extiende `sync_inbox_events`:

```text
attempts
max_attempts
last_attempt_at
next_retry_at
dead_lettered_at
conflict_id
```

Extiende `sync_conflicts`:

```text
resolved_by_user_id
resolution_note
updated_at
```

Estados soportados de inbox:

```text
received
processing
processed
duplicate
rejected
retry_pending
conflict
dead_letter
```

## Bootstrap

`GET /api/v1/sync/bootstrap` devuelve:

- tenantId
- storeId
- terminalId
- serverTime
- initialCursor
- terminal runtime
- access snapshot
- catalog snapshot
- inventory policy
- low-stock thresholds
- sync settings

## Auditoría

```text
sync.conflict.resolved
```

Ya existían:

```text
sync.push.ingested
sync.process.completed
```

## Notas

No se elimina `/sync/pull`; `bootstrap` es explícito para la instalación/inicialización del POS local.
Después del bootstrap, el POS debe usar `/sync/pull` con cursor para deltas.
