# PILOT-07 GO / NO-GO

## GO

PILOT-07 puede cerrarse como GO si el validador termina con:

```text
[PILOT-07] PILOT-07 PASS REAL PRODUCTION / GO
```

Y el resumen final incluye:

```text
databaseReady: True
requiredTablesPresent: True
monitoringContract: observability_metrics
dashboardMonitoring: ready
goNoGo: GO
```

## NO-GO

NO-GO si falla cualquiera de estos puntos:

- build dashboard
- self-test dashboard
- liveness/readiness
- admin login
- `/api/v1/observability/metrics`
- required tables
- cross-check SQL
- dependency endpoints para dashboard

## Logs requeridos

```text
Salida completa PowerShell desde [PILOT-07]
docs/pilot/logs/pilot-07-dashboard-operations-monitoring-log.md
```
