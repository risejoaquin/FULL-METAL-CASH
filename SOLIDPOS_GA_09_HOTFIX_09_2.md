# SolidPOS — GA-09 Hotfix 09.2

Fecha: 2026-08-23.

## Motivo

GA-09.1 ya corrigió el error de variable reservada de PowerShell, pero la corrida posterior bloqueó por p95 alto:

```text
Health load p95=3762ms p99=3852ms errors=0%
Protected read load p95=3266ms p99=3919ms errors=0%
GA-09 BLOCKED: health_p95_ms_3762, protected_p95_ms_3266
```

El diagnóstico manual por endpoint mostró que la API productiva sí responde dentro del SLA cuando se mide desde el mismo proceso de PowerShell:

```text
health-live p95=92ms
health-ready p95=666ms
sync-status p95=156ms
sync-contract p95=57ms
sales-list p95=274ms
dashboard-overview p95=802ms
```

Esto indica que el bloqueo de GA-09.1 fue causado por artefacto del validator: `Start-Job` crea procesos PowerShell separados para cada request concurrente, introduciendo overhead de cliente, inicialización, DNS/TLS/HTTP stack y scheduling que no representa latencia real del backend.

## Tipo de falla

Validator/script. No hay evidencia de defecto de backend, Railway, DB, auth, RLS, sync, dashboard ni schema v4.

## Cambio aplicado

- `scripts/ga/validate-ga-09-performance-capacity-resilience-offline-readiness.ps1`
  - Validator version actualizado a `GA-09.2-httpclient-concurrency-latency-measurement`.
  - Reemplazado el load runner basado en `Start-Job` por un runner interno basado en `.NET HttpClient` con concurrencia controlada por `SemaphoreSlim`.
  - La medición ahora registra latencia por request real dentro del mismo proceso, sin crear un proceso PowerShell por request.
  - Se conserva el contrato de salida: `name`, `method`, `path`, `statusCode`, `latencyMs`, `success`, `error`.

## Módulos afectados

- `scripts/ga`.
- Documentación/commands GA-09.

No se modificó:

- PosServer.
- PosDashboard.
- PosCore.
- PosBuilder.
- `database/migrations`.
- Contratos C#.

## Decisión técnica

Corregir el validator porque la evidencia manual contradice el resultado p95 de GA-09.1. El cuello no está demostrado en API ni DB; el problema era la metodología de medición concurrente del script.

## Riesgos

- GA-09.2 puede revelar un problema real si el p95 sigue alto usando HttpClient en proceso único.
- Si GA-09.2 bloquea con p95 alto, se debe pedir snapshot de Railway y logs productivos antes de tocar backend.
- No se debe relajar el SLA sin decisión formal.

## Criterio de cierre

GA-09 solo puede cerrar con:

```text
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```
