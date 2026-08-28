# Macro Fase 28 — Inventory Control Hardening

Estado: IMPLEMENTED — pending local validation

## Decisión arquitectónica

SolidPOS no debe tener una sola regla global de inventario. La política correcta para POS offline-first es configurable por tenant/store:

- `allow_negative_stock = true`: permite vender aunque el stock quede negativo.
- `allow_negative_stock = false` + `enforce_at_sale = true`: bloquea ventas online/cloud y transferencias que dejen negativo el stock de los productos afectados.
- `offline_sale_behavior = allow_and_reconcile | warn_and_reconcile | block_when_online`: deja explícito que offline puede permitir operar y reconciliar después; cloud puede bloquear con estado confiable.

Default dev/MVP: `allow_negative_stock = true` para no romper operación QSR ni pruebas históricas con stock negativo.

## Endpoints agregados

- `GET /api/v1/inventory/policy`
- `PATCH /api/v1/inventory/policy`
- `POST /api/v1/inventory/counts`
- `GET /api/v1/inventory/counts`
- `POST /api/v1/inventory/transfers`
- `GET /api/v1/inventory/transfers`
- `GET /api/v1/inventory/low-stock`

## DB

- `012_inventory_control_hardening.sql`
- `inventory_policies`
- `inventory_low_stock_thresholds`
- `inventory_counts`
- `inventory_count_lines`
- `inventory_transfers`
- `inventory_transfer_lines`

## Permisos

- `inventory.control`
- `inventory.count`
- `inventory.transfer`

## Auditoría

- `inventory.policy.updated`
- `inventory.count.completed`
- `inventory.transfer.completed`

## Regla de stock negativo

Ventas y transfers usan ledger append-only. La política se evalúa contra `inventory_stock` después de insertar los movimientos dentro de la misma transacción. Si la política bloquea stock negativo, la transacción se revierte.
