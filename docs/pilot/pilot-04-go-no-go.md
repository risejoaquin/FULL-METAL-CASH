# PILOT-04 GO / NO-GO

## GO

PILOT-04 = GO cuando:

- La venta controlada se completa.
- El recibo protegido coincide con la venta.
- El recibo digital queda activo.
- El recibo público responde por token.
- El email receipt stub se registra.
- La devolución completa queda `completed`.
- El refund cash queda `approved`.
- La venta pasa a estado `returned`.
- Hay compensación de inventario.
- Hay cash movement por refund.
- El turno cierra con diferencia cero.
- Auditoría contiene `receipt.issued`, `receipt.email_stub_queued` y `return.created`.
- SQL final reporta `PASS` y `GO`.

## NO-GO

Cualquier fallo en recibos, devolución, refund, inventario, caja, auditoría o SQL bloquea PILOT-05.
