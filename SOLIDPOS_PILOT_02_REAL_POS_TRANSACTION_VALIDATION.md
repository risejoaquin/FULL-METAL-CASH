# SolidPOS PILOT-02 — Real POS Transaction Validation

## Estado

PENDING USER VALIDATION

## Objetivo

Ejecutar y validar una venta real controlada en producción.

## Módulos afectados

- PosServer Sales API
- PosServer Cash Drawer API
- PosServer Receipts API
- PosServer Terminals API
- PosServer Audit API
- PosServer Inventory Ledger
- PosDashboard validation
- Pilot operational docs

## Archivos agregados

- `scripts/pilot/validate-real-pos-transaction.ps1`
- `scripts/pilot/pilot-02-transaction-check.sql`
- `docs/pilot/real-pos-transaction-validation.md`
- `docs/pilot/pilot-02-operator-checklist.md`
- `docs/pilot/pilot-02-go-no-go.md`
- `PILOT_02_VALIDATION_COMMANDS.md`

## Decisión arquitectónica

PILOT-02 valida la operación por API y confirma persistencia por SQL. Esto evita declarar GO únicamente por respuesta HTTP y confirma que la venta quedó consistente en:

- `pos.sales`
- `pos.sale_lines`
- `pos.payments`
- `pos.digital_receipts`
- `pos.inventory_ledger`
- `pos.audit_events`

## Riesgos controlados

- Turnos abiertos previos de la terminal PILOT-02 se cierran automáticamente antes de iniciar.
- La venta se identifica por terminal/fingerprint y referencia `pilot-02-real-pos-transaction`.
- El script valida cambio y diferencia de caja.
- El script falla si no hay inventario afectado o audit trail.

## Resultado esperado

`SolidPOS PILOT-02 real POS transaction validation completed.`

## Siguiente fase

`SolidPOS PILOT-03 — Cash Drawer / Shift Operations Validation`

## Hotfix 02.4

The sales read-model validation was hardened to use a production-safe lookup matrix instead of a single terminal-filtered query. This prevents false negatives after a real controlled sale has already been accepted and returned by the sale detail endpoint.
