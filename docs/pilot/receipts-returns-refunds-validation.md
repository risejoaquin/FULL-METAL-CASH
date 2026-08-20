# SolidPOS PILOT-04 — Receipts / Returns / Refunds Validation

## Objetivo

Validar en producción el circuito completo de recibos, devolución y reembolso contra una venta real controlada.

## Alcance operativo

- Preflight local de repositorio y secretos.
- Build/self-test de PosDashboard.
- Health productivo y readiness de base de datos.
- Login admin.
- Métricas protegidas.
- Lookup productivo de tienda, usuario, producto y precio.
- Registro/enrolamiento de terminal PILOT-04.
- Apertura de turno de caja.
- Venta real controlada en efectivo.
- Recibo protegido por venta.
- Emisión de recibo digital.
- Recibo digital protegido.
- Recibo público por token.
- Email receipt stub.
- Devolución completa de una línea.
- Refund en efectivo.
- Read model de devolución.
- Estatus de venta retornada.
- Cierre de turno con diferencia cero después del reembolso.
- Audit trail de recibo y devolución.
- Validación SQL de persistencia.
- Bitácora operativa PILOT-04.

## Criterio GO

PILOT-04 solo se considera GO si pasan todos los puntos anteriores y el SQL final devuelve:

```text
PILOT-04 receipts returns refunds validation PASS
GO
```

## Criterio NO-GO

Detener avance a PILOT-05 si falla cualquiera de estos puntos:

- Recibo protegido.
- Recibo digital activo.
- Recibo público.
- Email receipt stub.
- Creación de devolución.
- Refund aprobado.
- Movimiento de inventario por devolución.
- Cash movement de refund.
- Venta con estado `returned`.
- Auditoría.
- Cierre de turno con diferencia distinta de cero.
- SQL final.
