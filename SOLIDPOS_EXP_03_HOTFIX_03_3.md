# SolidPOS EXP-03 HOTFIX 03.3 — Inventory Stock SQL Cross-Check Contract

## Estado
PENDING USER VALIDATION

## Motivo
El validador EXP-03 llegó correctamente hasta la operación productiva completa de segunda terminal, pero falló en el SQL cross-check final porque el SQL consultaba `pos.inventory_current`, una relación que no existe en el contrato PostgreSQL real de SolidPOS.

## Corrección
Se auditó completo `scripts/expansion/exp-03-second-terminal-expansion-check.sql` y se eliminaron supuestos frágiles detectados en HOTFIX 03.1, 03.2 y 03.3:

- No usar `inventory_ledger.sale_id`.
- No comparar `uuid = text` sin cast explícito.
- No usar `pos.inventory_current`.
- Validar stock negativo usando el contrato real basado en ledger.

## Archivo corregido

- `scripts/expansion/exp-03-second-terminal-expansion-check.sql`

## Cambio principal
Antes:

```sql
SELECT count(*)
FROM pos.inventory_current ic
WHERE ic.tenant_id = p.tenant_id
  AND ic.quantity_on_hand < 0
```

Ahora:

```sql
WITH inventory_stock AS (
  SELECT l.tenant_id,
         l.store_id,
         l.product_id,
         l.variant_id,
         l.unit_id,
         sum(l.quantity_delta) AS quantity_on_hand
  FROM pos.inventory_ledger l
  JOIN params p ON p.tenant_id = l.tenant_id
  GROUP BY l.tenant_id, l.store_id, l.product_id, l.variant_id, l.unit_id
)
SELECT count(*)
FROM inventory_stock stock
WHERE stock.tenant_id = p.tenant_id
  AND stock.quantity_on_hand < 0
```

## Auditoría aplicada

Se revisó el SQL completo contra el contrato real de `database/postgresql/001_initial_schema_postgresql.sql`:

- `pos.inventory_ledger.reference_id` existe y es UUID.
- `pos.audit_events.entity_id` existe y es UUID.
- `pos.inventory_stock` existe como view derivada de `pos.inventory_ledger`.
- `pos.inventory_current` no existe.
- `pos.sales`, `pos.sale_lines`, `pos.payments`, `pos.cash_shifts`, `pos.digital_receipts`, `pos.sync_inbox_events`, `pos.sync_conflicts` y `pos.audit_events` existen.

## Resultado esperado

```text
[EXP-03] SQL second terminal production expansion cross-check PASS
[EXP-03] EXP-03 PASS SECOND TERMINAL PRODUCTION EXPANSION / GO EXP-04
```

## Alcance no tocado

- Backend no modificado.
- PosCore no modificado.
- Dashboard no modificado.
- PosBuilder no modificado.
- Migraciones no modificadas.
- Datos productivos no modificados.
