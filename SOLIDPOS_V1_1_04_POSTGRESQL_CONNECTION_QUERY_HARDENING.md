# SolidPOS V1.1-04 — PostgreSQL Connection & Query Hardening

**Baseline autorizado:** `6bda38f6f922fdb619132650d957a6f7a79b6b3c`
**Roadmap:** V1.1-04

## Implementación

- pool Npgsql unificado mediante la connection string normalizada: pooling activo, min/max acotados, idle pruning y reset-on-close;
- connect timeout y command timeout configurables con límites seguros;
- presupuesto explícito de 3 s para el listado de ventas, uno de los read paths críticos;
- visibilidad de consultas activas >= 500 ms sin exponer SQL ni parámetros;
- detección de sesiones `idle-in-transaction` (`idle in transaction` en PostgreSQL) >= 5 s;
- métricas Prometheus y alertas para slow queries e idle transactions;
- índices dirigidos a filtros de ventas y métricas de pagos;
- validator estático + producción con p95, idle transactions y server waits.

## Política por defecto

- `PostgreSql:Pool:MinSize = 0`
- `PostgreSql:Pool:MaxSize = 20`
- `PostgreSql:Pool:IdleLifetimeSeconds = 300`
- `PostgreSql:Pool:PruningIntervalSeconds = 10`
- `PostgreSql:Timeouts:ConnectSeconds = 10`
- `PostgreSql:Timeouts:CommandSeconds = 15`

Los valores son configurables y se acotan para impedir una configuración accidentalmente destructiva.

## Seguridad

La telemetría no devuelve texto SQL, parámetros, connection strings ni credenciales. Solo contadores y edades agregadas. `NoResetOnClose=false` preserva la limpieza de estado al devolver conexiones al pool.

## PASS

- restore/build/test/CI PASS;
- migración 020 aplicada;
- `/health/ready` p95 <= 1200 ms;
- `idleInTransactionCount = 0`;
- `activeNonClientWaitEventCount = 0`;
- las métricas de slow query están disponibles sin filtrar SQL.
