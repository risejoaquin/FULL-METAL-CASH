# SolidPOS BETA-03 — Beta Store Operations Validation

## Objetivo
Validar una jornada controlada de tienda beta con caja, venta, pago, recibo y cierre reconciliado de extremo a extremo.

## Flujo validado
1. Guardrails y secret scan.
2. `dotnet restore`, `build`, `test`.
3. Health/readiness y login administrativo.
4. Enrollment/registro de terminal controlada.
5. Apertura de turno de caja.
6. `cash_in`, `cash_out` y `drawer_open_no_sale`.
7. Dos ventas cash controladas asociadas al turno.
8. Reconciliación de `expectedCashCents`.
9. Cierre con `countedCashCents = expectedCashCents` y diferencia cero.
10. Audit trail de apertura/cierre/movimientos.
11. Emisión y lectura protegida/pública de recibo digital.
12. SQL source-of-truth final.

## Decisión técnica
BETA-03 reutiliza el flujo PILOT-03 ya probado como motor operacional y agrega la compuerta beta de recibo + reconciliación SQL. No duplica reglas de caja en un segundo script independiente.

## Blockers
- turno que no cierre;
- diferencia de caja distinta de cero;
- venta/pago no persistido;
- recibo no activo o inconsistente;
- auditoría faltante;
- SQL reconciliation `NO-GO`.

## Estado de entrega
`PASS BETA STORE OPERATIONS VALIDATION / GO BETA-04`
