# SolidPOS Iteration 10 — PosCore Offline Payment/Cash Drawer Runtime

## Estado

Preparada para validación local/remota.

## Objetivo

Cerrar el runtime local de caja de PosCore antes de WPF:

- turno de caja local SQLite;
- apertura/cierre local;
- pagos offline en efectivo;
- cálculo de cambio;
- cash in/cash out local;
- expected cash local;
- sync de venta offline al PosServer;
- conciliación contra cash shift remoto.

## Cambios principales

### PosCore Domain

- `LocalCashShift`
- `LocalCashMovement`
- `LocalSalePaymentSnapshot`
- `LocalCashShiftSummary`

### PosCore Application

- `LocalCashCalculator`
  - `CalculateChangeCents`
  - `CalculateExpectedCashCents`

### PosCore Infrastructure / SQLite

Nuevas tablas locales:

- `local_cash_shifts`
- `local_cash_movements`
- `local_sale_payments`

Nuevas operaciones runtime:

- abrir turno local;
- registrar cash in/cash out;
- registrar pago de venta offline;
- consultar resumen local;
- cerrar turno local con diferencia.

### PosCore CLI

Nuevos comandos:

- `open-local-shift`
- `cash-in`
- `cash-out`
- `cash-status`
- `close-local-shift`
- `sale-offline-from-cache-cash`

### Script E2E

- `scripts/poscore/validate-poscore-offline-cash-runtime.ps1`

Valida:

1. login admin productivo;
2. terminal enrollment/register;
3. SQLite local init/bind;
4. catálogo remoto hacia cache local;
5. apertura de cash shift remoto;
6. apertura de cash shift local;
7. cash in/out local y remoto;
8. venta offline cash desde SKU cacheado;
9. cálculo de cambio;
10. sync push/process;
11. venta materializada remota;
12. recibo digital;
13. cierre local y remoto;
14. comparación de resumen de caja;
15. pull/status/dead-letter.

## Criterio PASS

```text
build PASS
tests PASS
smoke remoto PASS
local cash shift opened
local cash in/out recorded
offline cash sale queued
change calculated
sync push/process PASS
remote sale materialized
local and remote expected cash match
local and remote differenceCents = 0
dead-letter = 0
```

## Decisión técnica

La caja local se guarda en SQLite como runtime operativo independiente de WPF. La venta sincronizada conserva el contrato `sale.completed` actual; el cash shift remoto se mantiene mediante los endpoints existentes de PosServer. La reconciliación de caja compara los totales locales contra el resumen remoto.
