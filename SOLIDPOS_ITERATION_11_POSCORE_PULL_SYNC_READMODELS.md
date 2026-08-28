# SolidPOS Iteration 11 — PosCore Pull Sync + Local Read Models

## Objetivo

Implementar el consumo real de `/api/v1/sync/pull` desde PosCore, aplicar cambios remotos de forma idempotente en SQLite local y guardar read models locales de ventas/remotos/recibos materializados.

## Alcance implementado

- Estado local de cursor de pull.
- Tabla local de cambios aplicados (`local_applied_changes`).
- Aplicación idempotente de cambios remotos por `change_id`.
- Read model local de ventas remotas (`local_remote_sales`).
- Read model local de recibos remotos (`local_remote_receipts`).
- Comandos CLI para pull, estado, guardado de venta/recibo y estado de read models.
- Script E2E: venta offline local, sync push/process, recibo remoto, pull sync, cursor/idempotencia, read models locales y diagnostics.
- Tests unitarios de `LocalSyncPullService`.

## Nuevas tablas SQLite

```text
local_sync_pull_state
local_applied_changes
local_remote_sales
local_remote_receipts
```

## Nuevos comandos PosCore CLI

```text
sync-pull
pull-status
save-remote-sale
save-remote-receipt
readmodel-status
```

## Decisión técnica

El pull sync guarda el stream remoto aplicado con cursor e idempotencia. Las ventas y recibos materializados se guardan como read models locales usando la respuesta remota verificada de PosServer, porque el servidor actual no publica `sale.completed` como `sync_change` para el mismo terminal que originó el evento. Esto conserva la separación: sync pull mantiene el estado/cursor de cambios y los read models locales almacenan entidades remotas ya confirmadas.

## Criterio PASS

- Build sin warnings/errors.
- Tests completos PASS.
- Smoke remoto PASS.
- PosCore crea venta offline con pago efectivo.
- PosServer procesa venta y emite recibo.
- PosCore ejecuta `/api/v1/sync/pull`.
- PosCore persiste cursor local.
- Segundo pull no duplica cambios.
- PosCore guarda read model local de venta remota.
- PosCore guarda read model local de recibo remoto.
- Dead-letter remoto queda en 0.
- Caja remota cierra con differenceCents = 0.
