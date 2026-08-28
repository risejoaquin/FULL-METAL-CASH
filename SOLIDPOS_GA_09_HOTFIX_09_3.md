# SolidPOS GA-09 Hotfix 09.3 — Endpoint Error Breakdown Diagnostics

Fecha: 2026-08-23.

## Estado

GA-09 sigue bloqueado hasta nueva evidencia real.

## Motivo

GA-09.2 corrigió la medición de latencia con HttpClient y dejó p95 dentro del umbral, pero el gate quedó bloqueado por tasa de error:

- health_error_percent_14.58
- protected_error_percent_30.56

El diagnóstico manual por endpoint mostró latencias sanas y 0 errores en modo secuencial, por lo que hace falta distinguir si bajo concurrencia el error viene de status HTTP real, rate limit, timeouts, DNS/TLS/cliente, o un endpoint específico.

## Cambio aplicado

Solo se modificó el validator:

- `scripts/ga/validate-ga-09-performance-capacity-resilience-offline-readiness.ps1`

Versión nueva:

- `GA-09.3-endpoint-error-breakdown-diagnostics`

## Detalle técnico

Se agregó desglose por endpoint para los bloques concurrentes:

- success/total por endpoint;
- error percent por endpoint;
- p95/p99 por endpoint;
- distribución de status codes;
- muestra de errores por endpoint cuando existan.

## Módulos afectados

- `scripts/ga`
- `docs/ga`

No se modificó:

- PosServer;
- PosDashboard;
- PosCore;
- PosBuilder;
- migraciones;
- contratos C#;
- schemaVersion;
- syncContract.

## Decisión técnica

Esto sigue siendo hotfix de validator/diagnóstico. No existe todavía evidencia suficiente para tocar backend o DB.

## Criterio de cierre

GA-09 solo podrá pasar cuando:

- p95 <= umbral configurado;
- p99 <= umbral configurado;
- error percent <= umbral configurado;
- DB post-load siga íntegra;
- RLS, schema v4 y sync contract no se degraden.
