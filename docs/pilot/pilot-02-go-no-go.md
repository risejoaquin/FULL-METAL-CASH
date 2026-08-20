# SolidPOS PILOT-02 — GO / NO-GO

## GO

El piloto puede avanzar a PILOT-03 si:

- backend productivo responde correctamente;
- base de datos productiva está lista;
- venta real controlada fue completada;
- pago en efectivo fue capturado;
- cambio calculado correctamente;
- inventario fue afectado;
- recibo digital protegido y público funcionan;
- audit trail existe;
- turno de caja cerró sin diferencia;
- bitácora de transacción fue generada.

## NO-GO

Detener si:

- hay error 5xx en producción;
- hay error de autenticación;
- la venta no queda en `completed`;
- la transacción queda incompleta;
- el pago no se registra;
- no hay movimiento de inventario;
- no se emite recibo;
- no hay evento de auditoría;
- la caja no cierra en cero;
- el SQL final no marca PASS.

## Acción ante NO-GO

1. No avanzar a PILOT-03.
2. Guardar logs completos.
3. Revisar el último bloque fallido.
4. Clasificar si es API, DB, dashboard, datos productivos o script.
5. Crear hotfix específico.
6. Repetir únicamente el bloque afectado si la base ya estaba validada.
