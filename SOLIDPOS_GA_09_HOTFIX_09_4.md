# SolidPOS — GA-09 Hotfix 09.4

## Nombre

GA-09.4 — Versioned HttpClient load tester isolation

## Fecha

2026-08-23

## Motivo

GA-09.3 agregó breakdown por endpoint y reveló respuestas 400 intermitentes durante carga concurrente, aunque las mismas rutas respondían correctamente en validación secuencial/manual.

El riesgo detectado fue que el helper C# embebido en PowerShell usaba el mismo nombre de tipo (`SolidPos.Ga09LoadTester`) entre hotfixes. En una misma sesión PowerShell, `Add-Type` no redefine tipos ya cargados; por lo tanto, una corrida posterior podía seguir ejecutando la implementación antigua aunque el script mostrara una versión nueva.

## Cambios

- Se cambió el helper embebido a tipos versionados:
  - `SolidPos.Ga09LoadTesterV094`
  - `SolidPos.Ga09MeasurementV094`
- Se aplica `Authorization` por request, no como header default compartido.
- Se fuerza `request.Version = 1.1` para evitar variaciones de negociación HTTP entre runtime/edge/proxy.
- Se lee el body de respuestas fallidas para imprimir evidencia diagnóstica útil.
- Se mantiene el breakdown GA-09.3 por endpoint/status.

## Módulos afectados

- `scripts/ga/validate-ga-09-performance-capacity-resilience-offline-readiness.ps1`
- documentación GA-09/hotfix

## Módulos no afectados

- PosServer
- PosDashboard
- PosCore
- PosBuilder
- migraciones PostgreSQL
- contratos C#
- `schemaVersion=4`
- `syncContract=schema_version_4`

## Decisión técnica

El bloqueo observado no justifica todavía modificar backend. Primero debe obtenerse una medición concurrente aislada y confiable dentro de la misma sesión PowerShell del usuario.

## Resultado esperado

La corrida debe mostrar:

```text
[GA-09] Validator version GA-09.4-versioned-httpclient-loadtester-isolation
```

y luego métricas por endpoint. Si los 400 desaparecen y p95/p99 quedan bajo umbral, GA-09 puede cerrar PASS. Si los 400 persisten, el body/error impreso permitirá clasificar backend, proxy, rate limit, DB readiness o contrato de endpoint.
