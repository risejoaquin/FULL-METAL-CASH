# SolidPOS Iteration 06 — PosCore Sync Processing + Conflict/Retry Runtime

## Objetivo

Cerrar el ciclo verificable de sincronización entre PosCore local SQLite y PosServer remoto:

- validar transición remota `received -> processed`;
- manejar duplicados reales desde PosCore;
- manejar retry local de eventos fallidos;
- guardar acknowledgements remotos;
- validar pull/status/dead-letter remoto;
- ejecutar ciclo completo local offline -> push -> process -> pull/status -> estado final.

## Decisiones aplicadas

1. Para validar transición semántica real, el E2E usa `pos.health_check`, porque el servidor lo procesa de forma determinística y no depende todavía de shape completo de venta remota.
2. La venta offline local sigue existiendo, pero el cierre fuerte de procesamiento remoto se hace con evento soportado y procesable.
3. Duplicado real se valida re-enviando el mismo `eventId` local que ya fue sincronizado.
4. Retry local se valida forzando un evento a `Failed`, regresándolo a `Pending` y sincronizándolo.
5. Dead-letter remoto se valida como endpoint operativo y lista consultable; no se fuerza dead-letter destructivo en producción.

## Cambios principales

- Nuevo comando PosCore CLI: `queue-health-check`.
- Nuevo comando PosCore CLI: `requeue-latest-synced`.
- Nuevo comando PosCore CLI: `fail-first-pending`.
- Nuevo comando PosCore CLI: `retry-failed`.
- `RemoteSyncPushService` marca eventos como `Failed` si el push remoto falla.
- `HttpRemoteSyncClient` interpreta `rejectedCount` del contrato de PosServer como fallo local.
- SQLite local agrega operaciones para reintento y requeue idempotente.
- Nuevo script E2E: `scripts/poscore/validate-poscore-sync-processing-retry.ps1`.
- Tests unitarios adicionales para duplicate y fallo remoto.

## Resultado esperado

```text
firstProcessedCount >= 1
retryProcessedCount >= 1
syncStatusDeadLetterCount = 0
deadLetterListCount = 0
message = PosCore sync processing/retry runtime completed.
```

## Cierre

La iteración se considera PASS REAL cuando pasan:

```text
dotnet restore
dotnet build
dotnet test
smoke remoto Railway
validate-poscore-sync-processing-retry.ps1
```
