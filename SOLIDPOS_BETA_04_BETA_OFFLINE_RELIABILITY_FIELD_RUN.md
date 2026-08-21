# SolidPOS BETA-04 — Beta Offline Reliability Field Run

## Objetivo
Validar en producción controlada que una terminal beta pueda operar una venta cash offline con autenticación local, catálogo e inventario cacheados, persistir outbox, reconectar, sincronizar hacia el servidor y recuperar cambios sin corrupción ni duplicados.

## Flujo validado
1. Guardrails, secret scan, restore/build/test.
2. Baseline SQL de dead-letter y conflictos pendientes.
3. Health/readiness, login admin y contrato sync `schemaVersion = 4` mediante el flujo PILOT-05 reutilizado.
4. Enrollment y registro de terminal controlada.
5. Bootstrap SQLite, binding, usuario local y login offline.
6. Cache local de catálogo e inventario.
7. Apertura de turno local/remoto.
8. Venta cash offline sin usar API durante la creación local.
9. Generación de outbox.
10. Reconnect + `sync-push` + `/api/v1/sync/process`.
11. Requeue/push duplicado para validar idempotencia.
12. Materialización remota única de venta y pago.
13. Emisión de recibo, pull sync y read models locales.
14. Cierre de caja sin diferencia.
15. SQL cross-check final: venta única, pago único, evento processed único, sin dead-letter nuevo, sin conflicto pendiente nuevo y sin eventos legacy.

## Decisiones técnicas
BETA-04 reutiliza PILOT-05 como motor E2E probado y agrega una compuerta de confiabilidad beta basada en baseline/final SQL. No se crea una segunda implementación del protocolo offline/sync.

La existencia de un dead-letter previo no hace fallar por sí sola la fase; queda como condición `preexisting_dead_letter_requires_triage`. Sí bloquea cualquier incremento durante el field run.

## Blockers
- venta remota faltante o duplicada;
- pago aprobado faltante o duplicado;
- evento `sale.completed` faltante o duplicado;
- nuevo dead-letter;
- nuevo conflicto pendiente;
- evento con schema distinto de 4;
- dead-letter en la terminal controlada;
- fallo de pull/read models o reconciliación de caja.

## Estado de entrega
`PASS BETA OFFLINE RELIABILITY FIELD RUN / GO BETA-05`
