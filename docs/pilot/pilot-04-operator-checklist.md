# PILOT-04 Operator Checklist

## Antes de ejecutar

- Confirmar que PILOT-01, PILOT-02 y PILOT-03 están cerrados como PASS REAL PRODUCTION / GO.
- Confirmar `DATABASE_URL` de Supabase activo en la sesión de PowerShell.
- Confirmar contraseña admin productiva cargada en `$securePassword`.
- Confirmar Railway `/health/ready` en estado `ready`.

## Durante la ejecución

- No interrumpir el script después de que abra turno de caja.
- Si falla después de abrir turno, enviar log completo desde `Opening cash shift` en adelante.
- Si falla después de crear la venta, enviar `saleId`.
- Si falla después de crear la devolución, enviar `returnId`, `saleId`, `receiptId` y salida SQL.

## Después de PASS

- Confirmar `goNoGo : GO`.
- Confirmar existencia de `docs/pilot/logs/pilot-04-receipts-returns-refunds-log.md`.
- Subir cambios a GitHub.
