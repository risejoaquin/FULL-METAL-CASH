# SolidPOS PILOT-02 — Real POS Transaction Validation

## Objetivo

Validar una transacción POS real controlada en producción, de punta a punta, usando el tenant productivo preparado en PILOT-01.

Esta fase ya no valida únicamente disponibilidad. Valida operación real:

- login admin productivo;
- registro de terminal POS controlada;
- apertura de turno de caja;
- venta real controlada;
- pago en efectivo;
- cálculo de cambio;
- persistencia de venta;
- línea de venta;
- pago capturado;
- movimiento de inventario;
- recibo digital protegido;
- recibo digital público;
- cierre de turno;
- diferencia de caja en cero;
- evento de auditoría;
- evidencia SQL final;
- bitácora operativa del piloto.

## Alcance

PILOT-02 usa los datos productivos ya confirmados:

- Tenant: `0ce5bbd0-528b-4aee-9fe3-93df001a4fde`
- Store: `MAIN`
- Producto: `QSR-AMERICANO`
- Método de pago: `cash`
- Dashboard: Reports / Operations / Audit
- Backend: Railway
- Base de datos: Supabase PostgreSQL

## Flujo operativo validado

1. Validar repositorio local y escaneo de secretos.
2. Compilar dashboard productivo y ejecutar self-test.
3. Validar `/health/live`.
4. Validar `/health/ready`.
5. Login admin.
6. Lookup PostgreSQL de store, admin, producto y precio.
7. Crear token de enrolamiento de terminal.
8. Registrar terminal controlada para PILOT-02.
9. Cerrar turnos abiertos antiguos de esa terminal de prueba.
10. Abrir turno de caja.
11. Crear venta controlada.
12. Validar total, pago y cambio.
13. Leer detalle de venta.
14. Confirmar movimientos de inventario.
15. Confirmar read model de venta.
16. Emitir recibo digital.
17. Leer recibo protegido.
18. Leer recibo público por token.
19. Validar resumen de caja.
20. Cerrar turno con diferencia cero.
21. Confirmar audit event `sale.completed`.
22. Confirmar persistencia vía SQL.
23. Crear bitácora `docs/pilot/logs/pilot-02-transaction-log.md`.

## Criterios GO

PILOT-02 solo es GO si se cumple todo:

- venta `completed`;
- `totalCents` correcto;
- `paidCents` correcto;
- `changeCents` correcto;
- al menos una línea de venta;
- al menos un pago capturado;
- al menos un movimiento de inventario;
- recibo digital activo;
- recibo público accesible;
- venta visible en read model;
- audit event `sale.completed` visible;
- turno cerrado;
- diferencia de caja igual a `0`;
- SQL final marca PASS.

## Criterios NO-GO

Cualquier punto es NO-GO:

- Railway no responde;
- readiness no está listo;
- login admin falla;
- terminal no se registra;
- no se puede abrir turno;
- venta no se completa;
- total/pago/cambio no coinciden;
- no hay movimiento de inventario;
- recibo protegido/público falla;
- turno no cierra;
- diferencia de caja distinta de cero;
- audit trail no aparece;
- SQL final falla.

## Resultado esperado

```text
[PILOT-02] Creating real controlled POS sale PASS
[PILOT-02] Validating sale detail and read model PASS
[PILOT-02] Issuing and validating digital receipt PASS
[PILOT-02] Validating shift summary and closing shift PASS
[PILOT-02] Validating audit event read model PASS
[PILOT-02] Validating transaction persistence via PostgreSQL PASS
[PILOT-02] Pilot transaction log initialized PASS

goNoGo  : GO
message : SolidPOS PILOT-02 real POS transaction validation completed.
```
