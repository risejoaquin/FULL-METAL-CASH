# SolidPOS — Roadmap Post-Pilot

Fecha: 2026-08-20
Estado base asumido: PILOT-01 a PILOT-10 cerrados como PASS REAL PRODUCTION / GO.
Decisión base: GO LIMITED PRODUCTION EXPANSION, no rollout masivo todavía.

## 1. Estado actual

SolidPOS ya superó el ciclo de piloto controlado en producción. Eso significa que el sistema validó, como mínimo:

- tenant/store productivo controlado.
- venta POS real.
- pagos cash y cambio.
- turnos/caja.
- recibos digitales.
- devoluciones/refunds.
- operación offline.
- recuperación/sync/conflictos.
- dashboard/observabilidad.
- backup/restore/rollback.
- runbook de incidentes.
- cierre de piloto y decisión de expansión.

Estado formal:

```text
SolidPOS Controlled Production Pilot = PASS REAL PRODUCTION / GO
SolidPOS Production Expansion = GO LIMITED EXPANSION
```

## 2. Cambio de etapa

El proyecto deja de estar en etapa de construcción/piloto y entra en etapa de expansión limitada.

La prioridad ya no es agregar features grandes. La prioridad es:

1. estabilizar operación real.
2. reducir riesgo operativo.
3. preparar onboarding repetible.
4. blindar observabilidad, soporte, backups y rollback.
5. validar una segunda tienda/terminal bajo control.
6. preparar el producto para venta comercial controlada.

## 3. Nuevo roadmap maestro

### EXP-01 — Post-Pilot Baseline Freeze

Objetivo: congelar una línea base estable después de PILOT-10.

Alcance:

- tag de release post-pilot.
- changelog completo PILOT-01 a PILOT-10.
- matriz de artefactos finales.
- consolidación de hotfixes piloto.
- limpieza de scripts duplicados o frágiles.
- actualización de reportes de avance.
- documentación de versión base.

Resultado esperado:

```text
SolidPOS EXP-01 — Post-Pilot Baseline Freeze = PASS
```

Gate:

- build PASS.
- tests PASS.
- secret scan PASS.
- zip release generado.
- tag/versionado definido.

---

### EXP-02 — Production Expansion Readiness Pack

Objetivo: convertir el resultado de PILOT-10 en un paquete operativo real para expansión limitada.

Alcance:

- checklist de expansión por tienda.
- checklist de expansión por terminal.
- runbook de onboarding.
- runbook de rollback por tienda.
- matriz GO/NO-GO diaria.
- matriz de monitoreo post-expansión.
- política de aceptación de nueva tienda/terminal.

Resultado esperado:

```text
SolidPOS EXP-02 — Production Expansion Readiness Pack = PASS
```

Gate:

- docs completas.
- validador de expansión listo.
- decisión GO LIMITED EXPANSION preservada.

---

### EXP-03 — Second Terminal Production Expansion

Objetivo: validar expansión real a una segunda terminal dentro del mismo tenant/store.

Alcance:

- terminal enrollment real.
- branding/config package.
- bootstrap sync.
- login/session local.
- venta real desde segunda terminal.
- shift/caja independiente.
- sync/offline controlado.
- dashboard monitoring por terminal.
- auditoría por terminal.

Resultado esperado:

```text
SolidPOS EXP-03 — Second Terminal Production Expansion = PASS REAL PRODUCTION / GO
```

Gate:

- segunda terminal operando sin romper la primera.
- separación de cash shifts.
- ventas aparecen correctamente por terminal.
- dashboard filtra/observa la nueva terminal.

---

### EXP-04 — Second Store Limited Expansion

Objetivo: preparar y validar una segunda tienda controlada del mismo tenant o tenant equivalente.

Alcance:

- alta de store.
- terminal inicial de store.
- catálogo inicial.
- inventario inicial.
- cash drawer policy.
- usuarios/roles/store access.
- operación de venta real.
- dashboard por store.
- reportes por store.
- audit trail por store.

Resultado esperado:

```text
SolidPOS EXP-04 — Second Store Limited Expansion = PASS REAL PRODUCTION / GO
```

Gate:

- multi-store real validado.
- no fuga cross-tenant ni cross-store.
- reportes correctos por store.

---

### EXP-05 — Operational Monitoring Hardening

Objetivo: pasar de monitoreo básico a monitoreo operativo de producción.

Alcance:

- dashboard de salud operacional.
- métricas de sync.
- métricas de offline queue.
- failed requests.
- p95 latency.
- dead letters.
- pending conflicts.
- failed payments.
- negative inventory.
- cash shift differences.
- alertas manuales o automáticas.

Resultado esperado:

```text
SolidPOS EXP-05 — Operational Monitoring Hardening = PASS
```

Gate:

- cada métrica crítica tiene owner, umbral y acción.
- dashboard operativo listo para rutina diaria.

---

### EXP-06 — Inventory Reconciliation Hardening

Objetivo: cerrar el riesgo residual de inventario antes de expansión más amplia.

Alcance:

- diagnóstico de negative inventory.
- conciliación ledger vs stock.
- ajuste controlado.
- reporte de diferencias.
- alertas de bajo/negativo stock.
- reglas de sustitución/modificadores verificadas.
- validadores SQL de inventario.

Resultado esperado:

```text
SolidPOS EXP-06 — Inventory Reconciliation Hardening = PASS
```

Gate:

- negative inventory explicado o corregido.
- ledger consistente.
- reglas de recetas/modificadores estables.

---

### EXP-07 — Sync SLA and Offline Reliability Hardening

Objetivo: convertir el sync/offline validado en una garantía operativa.

Alcance:

- SLA de sync.
- política de retry.
- política de dead letters.
- resolución de conflictos documentada.
- pruebas con reconexión prolongada.
- pruebas de duplicados/idempotencia.
- pruebas de terminal apagada.
- pruebas de outbox corrupto/controlado.

Resultado esperado:

```text
SolidPOS EXP-07 — Sync SLA and Offline Reliability Hardening = PASS
```

Gate:

- offline-first confiable para operación diaria.
- criterios claros de recuperación.

---

### EXP-08 — Support and Incident Operations

Objetivo: preparar soporte real para usuarios/tiendas.

Alcance:

- runbook de soporte nivel 1.
- runbook de soporte nivel 2.
- clasificación de incidentes.
- plantillas de reporte.
- checklist de evidencia.
- protocolo de escalación.
- protocolo de rollback.
- bitácora de incidentes.

Resultado esperado:

```text
SolidPOS EXP-08 — Support and Incident Operations = PASS
```

Gate:

- cualquier falla productiva tiene proceso de diagnóstico y contención.

---

### EXP-09 — Release Management and Update Channel

Objetivo: preparar releases controlados sin romper tiendas activas.

Alcance:

- versionado semántico.
- release notes.
- canal stable/candidate.
- actualización PosCore.
- actualización PosBuilder.
- migraciones DB seguras.
- rollback de release.
- smoke test post-release.

Resultado esperado:

```text
SolidPOS EXP-09 — Release Management and Update Channel = PASS
```

Gate:

- se puede actualizar sin intervención improvisada.
- cada release tiene rollback.

---

### EXP-10 — Customer/Admin Management Completion

Objetivo: convertir el panel administrativo en herramienta operativa real.

Alcance:

- gestión de usuarios.
- roles/permisos por tienda.
- gestión de terminales.
- gestión de stores.
- auditoría de cambios admin.
- operación segura desde dashboard.
- validación RBAC por acción.

Resultado esperado:

```text
SolidPOS EXP-10 — Customer/Admin Management Completion = PASS
```

Gate:

- administración básica del negocio sin SQL/manual work.

---

### EXP-11 — Catalog, Pricing and Promotions Operational Hardening

Objetivo: cerrar operación comercial diaria de catálogo/precios.

Alcance:

- edición de productos.
- precios por store si aplica.
- categorías.
- modificadores.
- recetas/BOM.
- promociones/cupones básicos si están en roadmap.
- sync hacia PosCore.
- pruebas offline con cambios de catálogo.

Resultado esperado:

```text
SolidPOS EXP-11 — Catalog Pricing Operations = PASS
```

Gate:

- cambios de catálogo no rompen POS/offline/sync.

---

### EXP-12 — Commercial Beta Readiness

Objetivo: preparar SolidPOS para beta comercial controlada.

Alcance:

- onboarding repetible.
- términos operativos.
- plan de soporte.
- plan de precios interno.
- demo package.
- documentación para operador.
- documentación para dueño/admin.
- métricas de éxito.
- criterios de salida de beta.

Resultado esperado:

```text
SolidPOS EXP-12 — Commercial Beta Readiness = PASS / GO BETA
```

Gate:

- listo para 2 a 5 clientes/tiendas controladas.
- no listo aún para rollout masivo sin operación de soporte.

## 4. Roadmap resumido por orden de ejecución

```text
EXP-01 Post-Pilot Baseline Freeze
EXP-02 Production Expansion Readiness Pack
EXP-03 Second Terminal Production Expansion
EXP-04 Second Store Limited Expansion
EXP-05 Operational Monitoring Hardening
EXP-06 Inventory Reconciliation Hardening
EXP-07 Sync SLA and Offline Reliability Hardening
EXP-08 Support and Incident Operations
EXP-09 Release Management and Update Channel
EXP-10 Customer/Admin Management Completion
EXP-11 Catalog Pricing Operations
EXP-12 Commercial Beta Readiness
```

## 5. Reglas de avance

No avanzar de una fase EXP a la siguiente si falla:

- build.
- tests.
- secret scan.
- health/readiness.
- admin login.
- protected metrics.
- SQL cross-check.
- dashboard build cuando aplique.
- sync status cuando aplique.
- audit trail cuando aplique.
- rollback/runbook cuando aplique.

## 6. Estado recomendado del proyecto

```text
Core Platform Completion: PASS
Controlled Production Pilot: PASS
Production Expansion Decision: GO LIMITED EXPANSION
Current Stage: EXP-01 Post-Pilot Baseline Freeze
Overall Completion Estimate: 84%
Commercial Beta Readiness Estimate: 65%
Mass Rollout Readiness Estimate: 45%
```

## 7. Siguiente acción inmediata

Iniciar:

```text
SolidPOS EXP-01 — Post-Pilot Baseline Freeze
```

Primer entregable esperado:

- ZIP completo post-pilot baseline.
- consolidación de hotfixes PILOT-01 a PILOT-10.
- reporte final de piloto.
- changelog.
- release tag recomendado.
- comandos de validación.
- GO/NO-GO para iniciar EXP-02.
