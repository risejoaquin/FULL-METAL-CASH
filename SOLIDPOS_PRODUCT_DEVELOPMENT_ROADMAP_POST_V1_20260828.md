# SolidPOS — Product Development Roadmap Post‑v1.0

**Empresa / Ingeniería:** SolidBit  
**Producto:** SolidPOS  
**Baseline de origen:** SolidPOS v1.0 QSR — Public GA  
**Estado de origen:** 100% cerrado, validado, desplegado y operando en Public GA  
**Fecha del roadmap:** 2026-08-28  
**Propietario técnico:** SolidBit Engineering  
**Modelo de entrega:** iterativo, full‑repo ZIP, validación local + producción, evidencia y hotfix cuando aplique  

---

## 1. Propósito

Este roadmap define la evolución de SolidPOS después del cierre formal de **SolidPOS v1.0 QSR**. El objetivo ya no es completar el MVP ni alcanzar General Availability: ese ciclo quedó terminado. A partir de este punto el trabajo se divide en dos líneas simultáneas:

1. **Operar, proteger y mantener SolidPOS v1.0 en producción.**
2. **Desarrollar nuevas versiones del producto sin degradar el baseline validado.**

El roadmap cubre cuatro releases principales:

- **SolidPOS v1.1 — Production Hardening & Operator Experience**
- **SolidPOS v1.2 — Commercial Feature Expansion**
- **SolidPOS v1.3 — Multi‑Store Scale & Management**
- **SolidPOS v2.0 — Platform Expansion & Architecture Evolution**

Cada release se divide en bloques de trabajo con objetivos, alcance, criterios de aceptación, módulos afectados y condiciones de salida.

---

# 2. Baseline actual — SolidPOS v1.0

## Estado global

| Área | Estado | Avance |
|---|---:|---:|
| PosServer / API | Cerrado | 100% |
| PosCore / WPF POS | Cerrado | 100% |
| PosBuilder | Cerrado | 100% |
| PosDashboard | Cerrado | 100% |
| PostgreSQL central | Cerrado | 100% |
| SQLite local | Cerrado | 100% |
| Offline / Sync | Cerrado | 100% |
| Auth / RBAC / Tenant / RLS | Cerrado | 100% |
| Sales / Payments / Receipts | Cerrado | 100% |
| Inventory / Modifier semantics | Cerrado | 100% |
| Pilot / Expansion / Beta | Cerrado | 100% |
| GA / CGA / LGA | Cerrado | 100% |
| Public GA Readiness | Cerrado | 100% |
| Public GA Activation | Cerrado | 100% |
| Post‑Activation Validation | Cerrado | 100% |
| Public GA Stability Burn‑In | Cerrado | 100% |
| Final Production Closure | Cerrado | 100% |

**Resultado:** `SOLIDPOS_V1_QSR_CURRENT_SCOPE_CLOSED`  
**Lifecycle:** `PRODUCTION_GA_OPERATION_AND_MAINTENANCE`

### Invariantes que no deben romperse

- `schemaVersion = 4` mientras no exista una migración/versionado formal aprobado.
- `syncContract = schema_version_4` mientras no exista una nueva versión compatible.
- Multi‑tenant isolation obligatorio.
- PostgreSQL RLS obligatorio.
- JWT + refresh rotation + RBAC obligatorio.
- Offline‑first con Outbox / Inbox e idempotencia.
- Inventario sin regresión de la semántica `none | add | substitute`.
- Los modificadores `substitute` deben respetar `replacesProductId`.
- No permitir descuentos, ventas, sync o inventario que produzcan inconsistencias financieras silenciosas.
- Public GA no se desactiva ni cambia de rollout stage por una fase de desarrollo normal.
- Todo cambio de esquema debe ser migrable, reversible y compatible con clientes desplegados.

---

# 3. Principios de desarrollo post‑v1

## 3.1 Reglas obligatorias

Cada bloque debe cumplir:

- Código real implementado, no documentación simulando funcionalidad.
- Build limpio.
- Tests unitarios, integración y contrato aplicables.
- Migraciones explícitas cuando corresponda.
- Validación de secretos.
- Validación multi‑tenant / RLS cuando toque datos tenant‑scoped.
- Validación offline/sync cuando toque eventos o read models.
- Validación de rollback cuando exista cambio operativo de riesgo.
- Entrega del repo completo en ZIP.
- Manifest del paquete.
- Archivo de comandos de validación.
- Evidencia del resultado esperado.
- Hotfix full‑repo cuando la implementación o validator falle.

## 3.2 Protocolo de entrega

Formato recomendado por bloque:

```text
solidpos-platform-v1-1-01-<slug>-YYYYMMDD.zip
solidpos-platform-v1-1-01-<slug>-YYYYMMDD.zip.sha256
V1_1_01_VALIDATION_COMMANDS.md
SOLIDPOS_V1_1_01_<TITLE>.md
V1_1_01_PACKAGE_MANIFEST.md
```

Si falla por código/script/contrato:

```text
solidpos-platform-v1-1-01-hotfix-01-<slug>-YYYYMMDD.zip
```

No se avanza al siguiente bloque hasta revisar el resultado de validación.

---

# 4. Línea permanente — Software Lifecycle Operations

Esta línea se ejecuta en paralelo a todas las versiones futuras y **no se considera backlog de desarrollo funcional**.

## OPS-01 — Production Health & SLO Monitoring

### Objetivo
Mantener una lectura continua de disponibilidad, latencia y capacidad.

### Alcance
- `/health/live`
- `/health/ready`
- latencias p50/p95/p99
- errores 4xx/5xx
- saturación del runtime
- métricas PostgreSQL
- sync queue health
- disponibilidad de dashboard

### Salida
- SLOs definidos y medibles.
- alertas accionables.
- runbook por alerta.

---

## OPS-02 — Database Maintenance & Capacity

- conexiones activas vs `ClientRead`
- waits reales de servidor
- slow queries
- índices faltantes
- vacuum/analyze
- crecimiento de tablas
- backup verification
- restore drill
- pool sizing

---

## OPS-03 — Security Operations

- dependency scanning
- secret rotation
- credential lifecycle
- JWT signing hygiene
- RBAC review
- RLS regression checks
- audit review
- security incident runbook

---

## OPS-04 — Release / Rollback / Update Operations

- canal estable
- staged rollout
- Velopack packages
- signing/provenance
- rollback package
- release notes
- deployment audit trail

---

## OPS-05 — Data Integrity & Financial Reconciliation

- ventas vs pagos
- refunds/returns
- receipts
- cash drawer reconciliation
- inventory negative stock
- audit completeness
- sync deadletter/conflicts

---

# 5. SolidPOS v1.1 — Production Hardening & Operator Experience

## Objetivo de release

Convertir el baseline v1.0 ya estable en una plataforma más observable, operable, diagnosticable y resistente a fallos reales de producción.

**Meta de release:** reducción de MTTR, mejor diagnóstico, mejor experiencia del operador y mayor confiabilidad sin ampliar todavía el dominio comercial de forma agresiva.

**Avance inicial:** 0%  
**Avance objetivo:** 100%

---

## V1.1-01 — Observability Foundation

### Objetivo
Instrumentar PosServer, sync y servicios críticos con telemetría estructurada.

### Implementar
- OpenTelemetry.
- tracing HTTP.
- tracing PostgreSQL.
- métricas de requests.
- métricas de errores.
- correlation IDs.
- tenant-safe diagnostic context.
- métricas de sync queues.

### Módulos
- PosServer.Api
- PosServer.Application
- PosServer.Infrastructure
- logging/observability docs

### PASS
- build/test PASS.
- traces sin secretos.
- tenant IDs no usados como fuga de datos.
- métricas disponibles en entorno local.
- endpoint crítico correlacionable end-to-end.

---

## V1.1-02 — Production Metrics & Alerting

### Implementar
- métricas Prometheus/OpenTelemetry export.
- error rate.
- request latency.
- PostgreSQL pressure.
- queue depth.
- retry/deadletter.
- negative stock alert.
- receipt/payment mismatch alert.

### PASS
- alert rules verificables.
- no alertas basadas en métricas semánticamente incorrectas.
- diferenciación entre `ClientRead` y server wait.

---

## V1.1-03 — Advanced Health / Readiness Diagnostics

### Implementar
- health dependency breakdown.
- DB latency.
- migration/schema compatibility.
- sync readiness.
- storage readiness.
- degraded vs unavailable state.

### PASS
- readiness sigue eficiente.
- no regresión del capacity gate.
- diagnóstico no filtra credenciales.

---

## V1.1-04 — PostgreSQL Connection & Query Hardening

### Implementar
- revisión de pool.
- command timeout policies.
- slow query visibility.
- índices de mayor impacto.
- query budget en endpoints críticos.
- detección de idle-in-transaction.

### PASS
- p95 dentro de baseline aprobado.
- 0 transacciones idle inesperadas.
- waits reales controlados.

---

## V1.1-05 — PosCore Error UX & Recovery

### Implementar
- errores legibles para operador.
- reintento seguro.
- estado offline/online visible.
- fallos de hardware diferenciados.
- acciones recuperables sin reiniciar la app.

### Módulos
- PosCore.Wpf
- PosCore.Application
- PosCore.Infrastructure

### PASS
- errores no técnicos para usuario final.
- logs técnicos conservados.
- no duplicación de venta/pago en retry.

---

## V1.1-06 — Sync Self-Healing

### Implementar
- retry policies mejoradas.
- jitter/backoff.
- poison event handling.
- automatic recovery.
- sync diagnostics.
- queue health summary.

### PASS
- pending/processing/retry converge a 0 en escenario recuperable.
- deadletters son explícitos.
- idempotencia conservada.

---

## V1.1-07 — Offline Diagnostics & Support Bundle

### Implementar
- export diagnostic bundle.
- versión app/schema/sync.
- estado SQLite.
- queue summary.
- last sync timestamps.
- hardware state.
- logs sanitizados.

### PASS
- bundle no contiene secretos.
- soporte puede reconstruir estado operativo básico.

---

## V1.1-08 — Terminal & Device Management Hardening

### Implementar
- terminal status.
- last-seen.
- version.
- store assignment.
- revoke/disable.
- device health.
- remote config metadata.

### PASS
- aislamiento tenant/store.
- terminal revocada no puede operar indebidamente.

---

## V1.1-09 — Update Channel & Velopack Hardening

### Implementar
- stable/beta channel.
- signed artifacts.
- staged rollout.
- rollback package.
- update health evidence.

### PASS
- upgrade y rollback reproducibles.
- DB/client compatibility preservada.

---

## V1.1-10 — Crash Reporting & Safe Telemetry

### Implementar
- crash capture.
- unhandled exception workflow.
- privacy filtering.
- opt-in operational telemetry donde aplique.

### PASS
- cero secretos/PII innecesaria.
- crash reproducible y correlacionable.

---

## V1.1-11 — PosDashboard Operations Center

### Implementar
- API health.
- DB health.
- sync queues.
- terminal status.
- incidents.
- version adoption.
- alert summary.

### PASS
- dashboard no reemplaza observability backend; consume modelos seguros.
- RBAC por permisos.

---

## V1.1-12 — v1.1 Release Closure

### Gate
- full build/tests.
- WPF validation.
- dashboard build.
- production smoke.
- capacity regression.
- sync regression.
- security regression.
- release package/provenance.

### Resultado
`SOLIDPOS_V1_1_PRODUCTION_HARDENING_CLOSED`

---

# 6. SolidPOS v1.2 — Commercial Feature Expansion

## Objetivo
Aumentar directamente el valor comercial del producto sin sacrificar consistencia financiera, offline-first ni multi-tenancy.

**Avance inicial:** 0%

---

## V1.2-01 — Discount Engine

- line discount.
- cart discount.
- percentage/fixed.
- authorization rules.
- audit.
- offline evaluation.

### PASS
Totales reproducibles entre POS/API/reportes.

---

## V1.2-02 — Promotions Engine

- buy X get Y.
- quantity tiers.
- category promotions.
- scheduled promotions.
- combinability rules.

---

## V1.2-03 — Coupon Management

- coupon codes.
- usage limits.
- validity.
- tenant/store scope.
- audit.
- offline-safe rules.

---

## V1.2-04 — Customer Profiles

- customer CRUD.
- contact data.
- purchase history.
- spend summary.
- consent fields.
- tenant isolation.

---

## V1.2-05 — Loyalty Program

- points ledger.
- earn/redeem.
- expiration rules.
- refunds reversal.
- offline conflict rules.

---

## V1.2-06 — Gift Cards / Stored Value

- issue.
- redeem.
- balance ledger.
- partial redemption.
- refund policy.
- anti-double-spend/idempotency.

---

## V1.2-07 — Scheduled Pricing & Price Lists

- effective dates.
- store overrides.
- price list assignment.
- historical price traceability.

---

## V1.2-08 — Combos / Bundles / Meal Deals

- combo definitions.
- component selection.
- modifier interaction.
- inventory consumption.
- price allocation.

---

## V1.2-09 — Daypart Menus & Availability

- breakfast/lunch/dinner windows.
- store timezone.
- product availability schedules.
- offline cached rules.

---

## V1.2-10 — Advanced Inventory Operations

- stock counts.
- adjustments.
- waste/spoilage.
- reasons.
- audit.
- variance reporting.

---

## V1.2-11 — Suppliers / Purchasing / Receiving

- suppliers.
- purchase orders.
- receiving.
- cost tracking.
- inventory movement linkage.

---

## V1.2-12 — Inventory Transfers

- transfer request.
- dispatch.
- receive.
- discrepancy.
- audit.
- multi-store consistency.

---

## V1.2-13 — Commercial Reporting Expansion

- discount reports.
- promo reports.
- customer metrics.
- loyalty liability.
- gift card liability.
- purchase/receiving reports.

---

## V1.2-14 — v1.2 Release Closure

### Resultado
`SOLIDPOS_V1_2_COMMERCIAL_EXPANSION_CLOSED`

---

# 7. SolidPOS v1.3 — Multi‑Store Scale & Management

## Objetivo
Llevar SolidPOS de una solución multi-store técnicamente capaz a una plataforma operativa para cadenas y grupos con administración central.

**Avance inicial:** 0%

---

## V1.3-01 — Organization / Store Hierarchy

- organización.
- regiones.
- stores.
- terminals.
- scoped administration.

---

## V1.3-02 — Central Catalog with Store Overrides

- global catalog.
- store enable/disable.
- store price override.
- modifier override rules.

---

## V1.3-03 — Central Pricing & Promotion Distribution

- publish configuration.
- effective version.
- acknowledgement.
- rollback.

---

## V1.3-04 — Multi‑Store Inventory Visibility

- stock by store.
- aggregated availability.
- transfer recommendations.
- stale-data indicators.

---

## V1.3-05 — Employee Multi‑Store Access

- store memberships.
- role per store.
- central roles.
- audit.

---

## V1.3-06 — Corporate Dashboard

- consolidated sales.
- store comparison.
- labor/operator metrics where supported.
- inventory variance.
- operational health.

---

## V1.3-07 — Consolidated Reporting

- organization totals.
- region/store drill-down.
- timezone-safe reporting.
- export.

---

## V1.3-08 — Remote Configuration Management

- versioned config.
- publish/acknowledge.
- rollback.
- terminal/store targeting.

---

## V1.3-09 — Fleet Management

- terminal inventory.
- software versions.
- last seen.
- remote disable.
- update cohorts.

---

## V1.3-10 — Multi‑Tenant Scale Testing

- tenant isolation under load.
- concurrency.
- RLS performance.
- noisy-neighbor testing.
- connection pool behavior.

---

## V1.3-11 — Disaster Recovery at Multi‑Store Scale

- tenant restore.
- environment restore.
- config restore.
- sync replay strategy.

---

## V1.3-12 — v1.3 Release Closure

### Resultado
`SOLIDPOS_V1_3_MULTI_STORE_SCALE_CLOSED`

---

# 8. SolidPOS v2.0 — Platform Expansion & Architecture Evolution

## Objetivo
Evolucionar SolidPOS de producto POS QSR a plataforma extensible, integrable y preparada para nuevos canales y verticales.

**Avance inicial:** 0%

> v2.0 no debe comenzar con una reescritura. Cada cambio arquitectónico debe tener una razón medible y una ruta de migración desde v1.x.

---

## V2.0-01 — Architecture Reassessment & ADR Freeze

- revisar Clean/Hexagonal boundaries.
- identificar deuda real.
- contratos públicos/privados.
- modularización.
- ADRs de v2.

---

## V2.0-02 — .NET 10 LTS Migration

- SDK/runtime.
- package compatibility.
- build pipeline.
- WPF compatibility.
- performance regression.

---

## V2.0-03 — Sync Contract vNext

- diseñar schema_version_5 solo si es necesario.
- compatibility window.
- version negotiation.
- migrations.
- event evolution.

---

## V2.0-04 — Public Integration API

- API keys/OAuth as appropriate.
- scopes.
- rate limits.
- webhook delivery.
- idempotency.
- developer documentation.

---

## V2.0-05 — Webhooks & Event Integration Layer

Eventos potenciales:
- sale.completed
- refund.completed
- inventory.changed
- customer.updated
- terminal.status_changed

---

## V2.0-06 — Integration Marketplace Foundation

- connector model.
- credentials vault strategy.
- per-tenant integration configuration.
- retries/deadletter.

---

## V2.0-07 — Kitchen Display System (KDS)

- kitchen tickets.
- routing.
- prep status.
- bump/reopen.
- offline behavior.

---

## V2.0-08 — Customer Order Display

- current order.
- totals.
- branding.
- optional promotional surface.

---

## V2.0-09 — Self‑Service Kiosk

- kiosk ordering.
- menu sync.
- modifier flow.
- payment integration abstraction.
- accessibility.

---

## V2.0-10 — Mobile / Handheld POS Exploration

- platform decision.
- shared contracts.
- offline storage.
- hardware feasibility.
- security model.

No implementación masiva hasta cerrar el architecture spike.

---

## V2.0-11 — E‑commerce / Online Ordering Integration

- menu publication.
- online order ingestion.
- order status.
- inventory considerations.
- idempotency.

---

## V2.0-12 — Delivery Platform Integration

- connector abstraction.
- order normalization.
- store routing.
- reconciliation.

---

## V2.0-13 — Advanced Analytics Platform

- analytical read models.
- historical aggregation.
- cohort/product/store analysis.
- BI exports.

---

## V2.0-14 — Verticalization Framework

Preparar reglas configurables para nuevos giros sin contaminar el dominio QSR:

- retail.
- restaurant/full service.
- services.
- specialty stores.

---

## V2.0-15 — v2 Security & Scale Gate

- threat model.
- penetration testing process.
- rate limiting.
- integration isolation.
- load testing.
- DR.
- observability.

---

## V2.0-16 — v2 Public GA Closure

### Resultado
`SOLIDPOS_V2_PLATFORM_BASELINE_CLOSED`

---

# 9. Dependencias entre releases

```text
SolidPOS v1.0 — CLOSED / PUBLIC GA
        |
        +--> Lifecycle Operations (permanente)
        |
        v
SolidPOS v1.1 — Production Hardening
        |
        v
SolidPOS v1.2 — Commercial Expansion
        |
        v
SolidPOS v1.3 — Multi-Store Scale
        |
        v
SolidPOS v2.0 — Platform Expansion
```

No es obligatorio implementar cada feature futura antes de comenzar investigación de la siguiente versión, pero **ningún release debe declararse cerrado mientras sus gates estén incompletos**.

---

# 10. Matriz de avance del roadmap futuro

| Release / Línea | Estado actual | Avance |
|---|---|---:|
| SolidPOS v1.0 QSR | CLOSED / PUBLIC GA | 100% |
| Software Lifecycle Operations | Activo permanente | Continuo |
| SolidPOS v1.1 | No iniciado | 0% |
| SolidPOS v1.2 | No iniciado | 0% |
| SolidPOS v1.3 | No iniciado | 0% |
| SolidPOS v2.0 | No iniciado | 0% |

El porcentaje global del **producto v1.0** permanece en 100%. Los porcentajes de v1.1+ son independientes y representan expansión futura, no deuda pendiente del baseline v1.0.

---

# 11. Criterios de calidad para todas las versiones

## Código
- `dotnet restore` PASS.
- `dotnet build` PASS sin errores.
- `dotnet test` PASS.
- frontend build PASS cuando aplique.

## Seguridad
- secret scan PASS.
- RBAC PASS.
- tenant isolation PASS.
- RLS PASS.
- no secretos en logs.

## Datos
- migraciones versionadas.
- rollback documentado.
- financial reconciliation PASS.
- negative stock = 0 salvo reglas expresamente soportadas en el futuro.

## Offline / Sync
- idempotency PASS.
- retry/recovery PASS.
- event version compatibility PASS.
- queue baseline sano.

## Producción
- health live/ready PASS.
- capacity gate sin degradación no aceptada.
- blockerCount = 0 al cerrar release.

---

# 12. Comandos estándar por entrega

## Restore

```powershell
dotnet restore solidpos-platform.sln
```

## Build

```powershell
dotnet build solidpos-platform.sln
```

## Tests

```powershell
dotnet test solidpos-platform.sln
```

## Migraciones

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

## Seed de desarrollo, si aplica

```powershell
.\scripts\apply-dev-auth-seed.ps1
```

## API local

```powershell
.\scripts\run-posserver-dev.ps1
```

Cada entrega deberá agregar sus comandos específicos de endpoints, DB, PosCore, PosBuilder o PosDashboard.

---

# 13. Política de versionado

## Patch — v1.x.y

Usar para:
- bug fixes.
- security patches compatibles.
- validator corrections.
- performance fixes sin cambio contractual mayor.

## Minor — v1.x

Usar para:
- nuevas capacidades compatibles.
- nuevas features comerciales.
- mejoras operativas importantes.

## Major — v2.0+

Usar para:
- contratos nuevos con migración relevante.
- cambios estructurales de plataforma.
- nuevas superficies de producto de gran alcance.

---

# 14. Política de compatibilidad

Antes de modificar cualquiera de estos elementos se requiere ADR y plan de migración:

- API contracts.
- PostgreSQL schema.
- SQLite schema.
- sync event schema.
- authentication claims.
- role/permission semantics.
- inventory semantics.
- financial calculation semantics.
- package/update compatibility.

Los clientes antiguos desplegados deben tener una ventana explícita de compatibilidad o bloqueo seguro.

---

# 15. Métricas de éxito del producto post‑v1

Además de “feature complete”, medir:

- uptime.
- p95/p99.
- error rate.
- MTTR.
- sync recovery time.
- deadletter rate.
- crash-free sessions.
- update success rate.
- support incidents por terminal.
- transaction reconciliation accuracy.
- inventory variance.
- release rollback rate.

En releases comerciales:

- ventas por tienda.
- ticket promedio.
- uso de promociones.
- repeat customers.
- loyalty engagement.
- inventory turns.

---

# 16. Riesgos principales del siguiente ciclo

| Riesgo | Mitigación |
|---|---|
| Romper v1 estable al añadir features | feature flags, tests de regresión y releases versionados |
| Complejidad excesiva en promociones | motor de reglas acotado y contratos deterministas |
| Doble gasto en loyalty/gift cards offline | ledger + idempotency + conflict rules |
| Desalineación cloud/local | versionado explícito de contratos y migraciones |
| Escala multi-store afectando RLS | load tests tenant-scoped y query plans |
| Reescritura prematura para v2 | ADRs y métricas antes de sustituir componentes |
| Integraciones externas inestables | adapter layer, retries, deadletter, circuit breaking |
| Observabilidad con fuga de datos | sanitización, data classification y tenant-safe telemetry |

---

# 17. Orden de ejecución recomendado

## Inmediato

1. Mantener **Software Lifecycle Operations** activo.
2. Iniciar **V1.1-01 Observability Foundation**.
3. Continuar secuencialmente v1.1 hasta su release closure.

## Después

4. Ejecutar v1.2 como expansión comercial.
5. Ejecutar v1.3 cuando se necesite escala operativa multi-store.
6. Iniciar v2 con arquitectura y compatibilidad, no con una reescritura indiscriminada.

---

# 18. Definition of Done por bloque

Un bloque está terminado únicamente cuando:

- implementación completa.
- documentación actualizada.
- tests completos.
- scripts de validación ejecutables.
- evidencia revisada.
- no existen blockers abiertos asociados al bloque.
- ZIP full-repo generado.
- SHA-256 generado.
- hotfixes incorporados al siguiente baseline.

---

# 19. Definition of Done por release

Una versión se declara cerrada cuando:

1. Todos sus bloques están PASS.
2. El repo final contiene todos los hotfixes acumulados.
3. No hay regresiones conocidas de seguridad, datos, sync ni finanzas.
4. Existe package manifest.
5. Existe release validation command set.
6. Existe evidencia de smoke/capacity/production aplicable.
7. `blockerCount = 0`.
8. Se crea tag Git de release.

Ejemplos:

```text
solidpos-v1.1.0
solidpos-v1.2.0
solidpos-v1.3.0
solidpos-v2.0.0
```

---

# 20. Estado oficial al emitir este roadmap

```text
Product: SolidPOS
Company: SolidBit
Current released baseline: SolidPOS v1.0 QSR
Development completion: 100%
Public GA: ACTIVE
Lifecycle: PRODUCTION_GA_OPERATION_AND_MAINTENANCE
Next development release: SolidPOS v1.1
Next implementation block: V1.1-01 OBSERVABILITY FOUNDATION
```

---

# 21. Crédito

**SolidPOS** es desarrollado y mantenido bajo la dirección de ingeniería de **SolidBit**.

**SolidBit — Engineering & Product Systems**  
Arquitectura, desarrollo, validación, producción y evolución del producto SolidPOS.

---

_End of roadmap._
