# LGA-03 — Limited GA Multi-Day Stability Burn-In

## Objetivo

LGA-03 ejecuta un burn-in multi-day de Limited GA sin activar Public GA. La fase valida que el sistema permanezca estable después de LGA-02: ventas completadas, pagos, recibos, sync, inventario, cash shifts, RLS, health checks, dashboard y presión de base de datos.

## Alcance

- Limited GA solamente.
- Public GA not activated.
- Burn-in multi-day por checkpoints diarios.
- No cambia schemaVersion.
- No cambia syncContract.
- No abre expansión pública.

## Condiciones de estabilidad

- completed sales en últimas 24h dentro del mínimo configurado.
- payments en últimas 24h dentro del mínimo configurado.
- receipts en últimas 24h dentro del mínimo configurado.
- sync conflicts must not increase sobre baseline aceptado.
- dead letter must not increase sobre baseline aceptado.
- negative stock debe permanecer en zero.
- open shifts deben permanecer en zero o dentro del límite configurado.
- Public GA not activated.

## Cierre

El checkpoint normal produce `PASS LGA-03 BURN-IN CHECKPOINT / CONTINUE LGA-03`.

La finalización con `-FinalizeBurnIn` solo debe usarse cuando existan los días requeridos del burn-in multi-day.
