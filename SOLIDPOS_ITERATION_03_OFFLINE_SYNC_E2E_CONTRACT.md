# SolidPOS Iteration 03 — Offline Sync End-to-End Server Contract

## Objetivo

Cerrar el contrato servidor para sincronización offline entre PosCore y PosServer: push, idempotencia, procesamiento, pull/bootstrap, diagnóstico operativo, dead-letter retry y validación E2E con token de terminal.

## Alcance implementado

- Endpoints operativos nuevos:
  - `GET /api/v1/sync/status`
  - `GET /api/v1/sync/contract`
  - `GET /api/v1/sync/dead-letter`
  - `POST /api/v1/sync/dead-letter/{inboxEventId}/retry`
- Migración `018_sync_e2e_contract_hardening.sql`.
- Columnas operativas para retry manual: `replayed_at`, `replay_reason`.
- Índices para diagnóstico por tenant/status/terminal/dead-letter.
- Servicio y repositorio de operaciones sync.
- Script E2E remoto `scripts/sync/validate-sync-e2e-contract.ps1`.
- Tests unitarios del contrato de operaciones sync.
- OpenAPI actualizado con los endpoints nuevos.

## Contrato sync soportado

Schema version actual: `4`.

Eventos inbound soportados:

- `pos.health_check`
- `sale.completed`
- `sale.voided`
- `inventory.adjustment.created`
- `cash.shift.opened`
- `cash.movement.created`
- `cash.shift.closed`

Estados de inbox:

- `received`
- `processing`
- `processed`
- `duplicate`
- `rejected`
- `retry_pending`
- `conflict`
- `dead_letter`

Estrategias de conflicto:

- `use_server`
- `use_client`
- `merge`
- `compensate`

## Decisión arquitectónica

La terminal offline puede enviar eventos mediante `sync.push` y leer cambios mediante `sync.pull`. Los diagnósticos globales y dead-letter retry quedan restringidos a permisos administrativos existentes:

- `sync.conflicts.read`
- `sync.conflicts.resolve`

No se agrega una nueva superficie de permisos porque la operación pertenece al mismo dominio de soporte de conflictos/dead-letter.

## Criterio de cierre

La iteración se considera cerrada cuando pasen:

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- migración local `018`
- migración remota `018` en Supabase
- smoke remoto con admin productivo
- `validate-sync-e2e-contract.ps1`
- GitHub Actions verde
- Railway deploy sano

## Hotfix 03.1

Se corrigió la migración 018 para usar `pos.sync_changes` en vez de la tabla inexistente `sync_outbox_changes`. Esta decisión mantiene alineado el contrato de pull con la implementación runtime actual del servidor.
