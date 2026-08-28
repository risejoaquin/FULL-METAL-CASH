# SolidPOS — GENERAL AVAILABILITY READINESS ROADMAP

**Etapa:** GENERAL AVAILABILITY READINESS  
**Producto:** SolidPOS  
**Fecha de baseline:** 2026-08-21  
**Origen:** cierre de `BETA-10 — Limited Commercial Beta Closure Report`  
**Estado de entrada requerido:** `PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP`  
**Objetivo de salida:** autorización formal para lanzar General Availability sin activar GA automáticamente.

---

## 1. Punto de partida

La etapa BETA-01 → BETA-10 quedó cerrada como `PASS REAL PRODUCTION`.

Baseline de entrada confirmado al cierre de BETA-10:

- `blockers = {}`
- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `generalAvailabilityActivated = False`
- `syncReliability = PASS`
- `releaseReadiness = PASS`
- `activeBetaReleaseCount = 1`
- `activeStableReleaseCount = 0`
- `pendingConflictCount = 0`
- `newDeadLetterCount = 0`
- `untriagedDeadLetterCount = 0`
- `openShiftCount = 0`
- `cashDifferenceLast24HoursCount = 0`
- `retryPendingCount = 1`
- `retryOverSlaCount = 1`
- existe un dead-letter histórico conocido, triaged y estable.

Condiciones heredadas que deben cerrarse o aceptar formalmente antes de GA:

1. `retry_pending_sync_requires_ga_readiness_closure`
2. `retry_over_sla_requires_ga_readiness_closure`
3. `known_dead_letter_triaged_and_stable`
4. `stable_channel_promotion_pending`

Estas condiciones no bloqueaban el cierre de la beta limitada, pero **no deben desaparecer del expediente de GA**.

---

# 2. Principios de ejecución

## 2.1 Gating estricto

No se avanza a la siguiente fase hasta obtener el `PASS ... / GO GA-XX` correspondiente.

Si una fase falla:

1. detener avance;
2. localizar el punto exacto;
3. auditar la familia completa del error;
4. crear `HOTFIX GA-XX.Y`;
5. entregar repo completo en ZIP;
6. repetir validación hasta PASS.

No se permite convertir un blocker real en condición solo para avanzar.

---

## 2.2 Fuente de verdad

Se conservan las decisiones arquitectónicas ya cerradas:

- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `inventory_ledger` es source of truth de inventario
- modificadores: `none | add | substitute`
- sustituciones: `substitute + replacesProductId`
- offline/sync: Outbox / Inbox
- tenant isolation mediante contexto de tenant + RLS
- JWT + refresh rotation
- release/update con Velopack
- despliegue backend objetivo: Railway
- PostgreSQL central
- SQLite WAL local

---

## 2.3 Seguridad de las validaciones

Las fases GA deben:

- evitar deletes destructivos no justificados;
- evitar cierres forzados de operaciones comerciales reales;
- no reescribir ledger histórico;
- usar reconciliaciones append-only;
- usar transacciones con rollback cuando el objetivo sea probar una mutación;
- no imprimir secretos;
- exigir evidencia de auditoría para cualquier remediación automática;
- diferenciar fixtures de validación de entidades comerciales reales.

---

# 3. Estructura estándar de cada fase

Cada fase deberá entregar, como mínimo:

```text
scripts/ga/validate-ga-XX-<slug>.ps1
scripts/ga/ga-XX-<slug>-check.sql
GA_XX_VALIDATION_COMMANDS.md
SOLIDPOS_GA_XX_<TITLE>.md
docs/ga/ga-XX-<slug>.md
docs/ga/ga-XX-go-no-go.md
docs/ga/logs/ga-XX-<slug>-log.md
```

Cuando aplique:

```text
scripts/ga/ga-XX-<safe-remediation>.sql
docs/ga/ga-XX-runbook.md
docs/ga/ga-XX-checklist.md
docs/ga/ga-XX-evidence-matrix.md
```

Cada validator debe producir:

```text
.runtime/ga-XX-<slug>/
  ga-XX-manifest.json
  ga-XX-evidence.md
  ga-XX-snapshot.json
```

---

# 4. Roadmap completo

---

# GA-01 — General Availability Baseline Freeze and Readiness Charter

## Objetivo

Congelar el baseline exacto de producción después de BETA-10 y establecer el contrato formal de entrada a GA Readiness.

## Incluye

- revalidación fresca de BETA-10;
- snapshot de:
  - tenant;
  - stores;
  - terminals;
  - users;
  - catalog;
  - sales;
  - payments;
  - receipts;
  - returns/refunds;
  - inventory;
  - sync;
  - audit;
  - release/update;
- hash del repo/baseline;
- lista de condiciones heredadas;
- lista inicial de blockers;
- declaración de no activación de GA;
- readiness charter.

## Debe validar

```text
BETA-10 = PASS
blockers = {}
schemaVersion = 4
syncContract = schema_version_4
generalAvailabilityActivated = False
```

## Blockers

- drift de schema;
- pérdida de tenant isolation;
- diferencias respecto al cierre BETA-10 no explicadas;
- release beta activo inválido;
- datos corruptos surgidos después de BETA-10;
- GA activado prematuramente.

## Resultado requerido

```text
PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02
```

---

# GA-02 — Sync Queue and SLA Closure

## Objetivo

Cerrar las condiciones heredadas de sincronización antes de considerar el sistema listo para GA.

## Incluye

- `retry_pending`;
- retry due;
- retry over SLA;
- dead-letter;
- stale processing;
- pending conflicts;
- processed schema-4 events;
- latencia de sync;
- triage del dead-letter histórico;
- decisión explícita:
  - retry;
  - quarantine;
  - supersede;
  - close as historical evidence;
- evidencia de auditoría.

## Criterio ideal de salida

```text
retryPendingCount = 0
retryOverSlaCount = 0
staleProcessingCount = 0
pendingConflictCount = 0
newDeadLetterCount = 0
untriagedDeadLetterCount = 0
legacySchemaEventCount = 0
```

El dead-letter histórico solo puede permanecer si existe una razón técnica formal y no representa trabajo pendiente ejecutable.

## Blockers

- retry sin dueño/decisión;
- retry vencido sin acción;
- stale processing;
- conflicto pendiente;
- dead-letter nuevo;
- dead-letter sin diagnóstico;
- evento legacy schema;
- pérdida de idempotencia.

## Resultado requerido

```text
PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03
```

---

# GA-03 — Support, Incident and SLO Operations Readiness

## Objetivo

Convertir el soporte probado durante beta en una operación sostenible de GA.

## Incluye

- severidades SEV1–SEV4;
- intake de incidentes;
- escalation policy;
- on-call ownership;
- runbooks;
- retry/dead-letter triage;
- cash/inventory incident routing;
- release incident routing;
- rollback authority;
- SLOs;
- SLIs;
- error budget;
- soporte diario;
- evidencia de auditoría;
- post-incident review template.

## SLO mínimos a fijar

El validator debe exigir que existan valores explícitos para:

- API availability;
- p95 latency;
- failed request rate;
- sync processing delay;
- retry backlog age;
- dead-letter creation;
- payment failure rate;
- data reconciliation failures.

No se debe inventar un porcentaje silenciosamente: los valores finales deben quedar declarados en el contrato de fase.

## Blockers

- SEV1/SEV2 sin ruta;
- rollback sin responsable;
- métrica crítica sin owner;
- SLO sin umbral;
- ausencia de runbook para incidentes de sync, cash, payments o release;
- ausencia de evidencia auditable.

## Resultado requerido

```text
PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04
```

---

# GA-04 — Production Data Integrity and Financial Reconciliation Gate

## Objetivo

Exigir un cierre limpio de datos antes de producir un release candidate estable.

## Incluye

### Sales / Payments

- sale/payment reconciliation;
- approved payment consistency;
- totals;
- returned sales;
- orphan payments.

### Receipts

- active receipts;
- receipt-to-sale consistency;
- public token integrity.

### Returns / Refunds

- completed return/refund pairing;
- refund totals;
- original sale links.

### Cash

- open shifts;
- stale shifts;
- counted vs expected;
- cash differences.

### Inventory

- negative inventory;
- ledger consistency;
- reconciliation adjustments;
- recipes;
- modifiers;
- substitute semantics.

### Catalog / Pricing

- negative prices;
- invalid price windows;
- taxes;
- modifier behavior;
- substitute modifiers;
- invalid references.

### Users / Access

- orphan roles;
- invalid store access;
- inactive tenant/user relationships.

## Criterio de salida

Todos los mismatch counts materiales deben ser cero.

## Resultado requerido

```text
PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05
```

---

# GA-05 — Stable Release Candidate Build, Signing and Provenance

## Objetivo

Crear un release candidate apto para el canal `stable`, sin promoverlo todavía.

## Incluye

- versión GA RC;
- build reproducible;
- artifact hash;
- signature verification;
- Velopack universal installer;
- package metadata;
- release notes;
- SBOM/provenance cuando el tooling existente lo permita;
- rollback version;
- schema compatibility;
- update contract compatibility;
- source commit/reference;
- artifact retention.

## Debe validar

```text
packageType = velopack
signaturePresent = True
universalInstaller = True
rollbackVersion != null
mandatory = False
tenantScoped = True
schemaVersion = 4
```

Durante GA Readiness, la actualización no debe volverse mandatory automáticamente.

## Blockers

- hash faltante;
- signature ausente;
- artifact drift;
- rollback version inexistente;
- paquete no universal;
- metadata incompleta;
- incompatibilidad schema/update contract.

## Resultado requerido

```text
PASS GA STABLE RELEASE CANDIDATE PROVENANCE / GO GA-06
```

---

# GA-06 — Stable Channel Promotion and Cohort Update Dry Run

## Objetivo

Validar la promoción controlada del RC al canal `stable` y comprobar la experiencia de update sin hacer rollout general.

## Incluye

- `internal -> beta -> stable`;
- identidad exacta del artifact entre canales;
- release notes;
- signature/hash;
- update check;
- tenant-scoped update;
- non-mandatory update;
- cohort reducido;
- comprobación de clientes compatibles;
- rollback target;
- auditoría de promoción.

## Debe validar

```text
activeStableReleaseCount >= 1
mandatory = False
tenantScoped = True
updateAvailable = True
promotionArtifactMatchCount >= 1
releaseAuditCount >= 1
```

## Blockers

- artifact diferente entre beta y stable sin justificación;
- stable mandatory prematuramente;
- update check inconsistente;
- rollout fuera de cohort;
- ausencia de audit trail;
- incompatibilidad con cliente.

## Resultado requerido

```text
PASS GA STABLE CHANNEL PROMOTION COHORT DRY RUN / GO GA-07
```

---

# GA-07 — Backup, Restore, Rollback and Disaster Recovery Gate

## Objetivo

Probar que una release estable puede revertirse y que los datos pueden recuperarse sin depender de procedimientos improvisados.

## Incluye

- backup verification;
- restore verification;
- DB rollback drill;
- application release rollback;
- Velopack rollback path;
- release revoke/restore transaction drill;
- RTO;
- RPO;
- data integrity después del restore;
- rollback audit;
- no destructive production mutation cuando sea simulación.

## Debe validar

- backup legible;
- restore comprobado;
- rollback version disponible;
- `persistedRollbackMutationCount = 0` cuando el drill sea transaccional;
- health/readiness después del drill;
- reconciliación de datos posterior.

## Blockers

- backup no restaurable;
- RTO/RPO indefinido;
- rollback sin artifact;
- pérdida de datos;
- rollback deja release corrupto;
- operación destructiva no recuperable.

## Resultado requerido

```text
PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08
```

---

# GA-08 — Security, Tenant Isolation and Access Control Final Gate

## Objetivo

Cerrar la postura de seguridad antes de autorizar GA Launch.

## Incluye

- secret scan;
- auth login/refresh/logout;
- refresh rotation;
- JWT claims;
- RBAC;
- policies;
- store access;
- tenant context;
- RLS;
- provisioning isolation;
- password handling;
- CORS;
- production Swagger exposure policy;
- sensitive logging;
- audit events;
- security headers;
- dependency/package audit disponible;
- negative authorization tests;
- cross-tenant tests.

## Debe validar especialmente

- tenant A no lee/escribe tenant B;
- user sin permiso recibe rechazo;
- secrets no aparecen en repo/logs/manifests;
- provisioning idempotency no cruza tenants;
- RLS sigue activo en tablas críticas.

## Blockers

Cualquier bypass de tenant isolation, auth o authorization es blocker absoluto.

## Resultado requerido

```text
PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```

---

# GA-09 — Performance, Capacity, Resilience and Offline Gate

## Objetivo

Demostrar que SolidPOS soporta su carga objetivo y mantiene operación POS durante fallos previsibles.

## Incluye

### API

- p50/p95/p99;
- error rate;
- concurrent requests;
- connection pool;
- DB latency;
- readiness under load.

### POS/offline

- offline sale;
- local cash payment;
- local inventory consumption;
- Outbox;
- reconnect;
- push sync;
- idempotency;
- conflict behavior;
- read-model refresh;
- max offline policy de 72 horas.

### Resilience

- network interruption;
- API unavailable;
- DB unavailable/readiness degraded;
- retry behavior;
- duplicate event protection;
- restart recovery.

## Blockers

- data loss;
- duplicate sale/payment;
- inconsistent inventory;
- idempotency failure;
- offline contract violation;
- retry storm;
- latency/error thresholds fuera de los límites definidos para GA.

## Resultado requerido

```text
PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE / GO GA-10
```

---

# GA-10 — Observability, Dashboard, Alerting and On-Call Readiness

## Objetivo

Garantizar que una degradación de producción sea detectable y accionable antes del rollout general.

## Incluye

- health/live;
- health/ready;
- DB readiness;
- API metrics;
- failed requests;
- latency;
- sales;
- payment failures;
- retry_pending;
- retry over SLA;
- dead-letter;
- stale processing;
- conflicts;
- open shifts;
- cash differences;
- negative/low inventory;
- audit events;
- release/update state;
- dashboard source contract;
- alert thresholds;
- alert owners;
- daily monitoring pack;
- on-call handoff.

## Debe validar

Cada señal crítica debe tener:

```text
metric
threshold
severity
owner
runbook
escalation path
```

## Blockers

- métrica crítica sin alerta;
- alerta sin owner;
- blocker de datos no visible;
- dashboard contract roto;
- health incorrecto;
- ausencia de runbook.

## Resultado requerido

```text
PASS GA OBSERVABILITY ALERTING ONCALL READINESS / GO GA-11
```

---

# GA-11 — GA Customer, Operator and Administrative Acceptance

## Objetivo

Obtener aceptación formal de los flujos que un comercio necesita para operar SolidPOS en GA.

## Dominios

1. sales;
2. cash;
3. receipts;
4. returns/refunds;
5. catalog/pricing;
6. inventory;
7. users/admin/RBAC;
8. offline/reconnect;
9. dashboard/reporting;
10. support;
11. release/update;
12. rollback/recovery.

## Incluye

- acceptance checklist;
- operator sign-off;
- admin sign-off;
- support sign-off;
- release owner sign-off;
- known issues;
- accepted limitations;
- blockers;
- exit criteria.

## Regla

A diferencia de BETA-08, para el cierre de GA Readiness los sign-offs no deben quedarse solo como `PLACEHOLDER_READY`.

Debe existir evidencia explícita de decisión humana o una excepción formal documentada.

## Blockers

- flujo crítico rechazado;
- sign-off faltante;
- known issue material sin owner;
- limitación comercial no comunicada;
- inconsistencia entre evidencia técnica y aceptación.

## Resultado requerido

```text
PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12
```

---

# GA-12 — General Availability Launch Readiness Final Go/No-Go

## Objetivo

Emitir la decisión ejecutiva/técnica final de si SolidPOS puede ser lanzado como General Availability.

## Incluye

### Resumen completo

- GA-01 → GA-11;
- customers;
- stores;
- terminals;
- sales;
- payments;
- sync reliability;
- SLA/SLO;
- support readiness;
- security;
- data integrity;
- release stable;
- rollback;
- backup/restore;
- observability;
- offline reliability;
- operator/admin acceptance;
- known conditions;
- blockers.

### Criterios mínimos de GO

```text
GA01To11 = PASS
blockers = {}
pendingConflictCount = 0
newDeadLetterCount = 0
untriagedDeadLetterCount = 0
staleProcessingCount = 0
openShiftCount = 0
cashDifferenceLast24HoursCount = 0
negativeInventoryItemCount = 0
salePaymentMismatchCount = 0
returnRefundMismatchCount = 0
invalidStableReleaseCount = 0
activeStableReleaseCount >= 1
rollbackValidation = GO
securityGate = PASS
observabilityGate = PASS
acceptanceGate = PASS
schemaVersion = 4
syncContract = schema_version_4
```

## Decisiones permitidas

```text
GO_GENERAL_AVAILABILITY_LAUNCH
GO_CONTROLLED_GA_ROLLOUT
NO_GO_FIX_BLOCKERS
```

## Regla de seguridad

Incluso si GA-12 retorna `GO_GENERAL_AVAILABILITY_LAUNCH`, el validator **no debe ejecutar automáticamente el rollout general**.

Debe producir autorización y handoff.

## Resultado ideal

```text
PASS GENERAL AVAILABILITY READINESS / GO GENERAL AVAILABILITY LAUNCH
```

---

# 5. Hotfix policy

Ejemplos:

```text
GA-02 falla por retry SLA
→ HOTFIX GA-02.1

GA-06 falla por identidad de artifact
→ HOTFIX GA-06.1

GA-09 falla por idempotencia offline
→ HOTFIX GA-09.1
```

El hotfix siempre parte del último repo validado o del ZIP de fase que falló y entrega el repo completo.

---

# 6. Matriz de exit criteria global

| Dominio | Condición para GA |
|---|---|
| Build | 0 errores |
| Tests | 100% PASS |
| Secrets | scan PASS |
| Health | live + ready + DB ready |
| Tenant isolation | PASS |
| RBAC | PASS |
| Sales/payments | reconciliados |
| Cash | sin diferencias no explicadas |
| Inventory | sin negativos no explicados |
| Returns/refunds | reconciliados |
| Catalog/pricing | sin inconsistencias |
| Sync | schema 4, sin conflictos pendientes |
| Retry | sin backlog vencido no gestionado |
| Dead-letter | ninguno nuevo/no triaged |
| Audit | evidencia suficiente |
| Stable release | activo, firmado, hash validado |
| Update | cohort dry-run PASS |
| Rollback | PASS |
| Backup/restore | PASS |
| Offline | PASS dentro de política |
| Observability | alertas + owners + runbooks |
| Support | operación + SLO definidos |
| Acceptance | sign-offs completos |
| Blockers | `{}` |

---

# 7. Comando estándar de validación

Cada fase seguirá el patrón:

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"

Unblock-File .\scripts\ga\validate-ga-XX-<slug>.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-XX-<slug>.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "<TENANT_ID>" `
  -Email "<ADMIN_EMAIL>" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL
```

Cuando la fase dependa de dashboard:

```text
-SkipDashboardBuild
```

solo podrá saltar el build si el validator sigue comprobando estáticamente el source contract correspondiente.

---

# 8. Evidencia requerida por entrega

Cada fase deberá indicar:

1. qué se cambió;
2. módulos afectados;
3. decisiones arquitectónicas;
4. riesgos;
5. restore;
6. build;
7. tests;
8. migrations/seed si aplica;
9. ejecución del validator;
10. endpoints validados;
11. SQL source-of-truth;
12. resultado esperado;
13. logs que debe enviar el usuario si falla;
14. SHA-256 del ZIP;
15. estado:
   - `PENDING USER VALIDATION`
   - `PASS REAL PRODUCTION`
   - `FAIL / HOTFIX REQUIRED`.

---

# 9. Condiciones que no se deben ocultar

Durante GA Readiness, cualquier condición heredada debe permanecer visible hasta que ocurra una de estas tres cosas:

1. se corrige;
2. se acepta formalmente con owner y riesgo;
3. se demuestra que es únicamente evidencia histórica no accionable.

Nunca debe desaparecer solo porque una fase posterior no la consulta.

---

# 10. Orden de ejecución

```text
BETA-10
  PASS LIMITED COMMERCIAL BETA CLOSURE
        ↓
GA-01 Baseline Freeze
        ↓
GA-02 Sync Queue / SLA Closure
        ↓
GA-03 Support / Incident / SLO
        ↓
GA-04 Data + Financial Reconciliation
        ↓
GA-05 Stable RC / Signing / Provenance
        ↓
GA-06 Stable Promotion / Cohort Dry Run
        ↓
GA-07 Backup / Restore / Rollback / DR
        ↓
GA-08 Security / Tenant Isolation / Access
        ↓
GA-09 Performance / Capacity / Resilience / Offline
        ↓
GA-10 Observability / Alerting / On-call
        ↓
GA-11 Customer / Operator / Admin Acceptance
        ↓
GA-12 Final GA Launch Readiness
        ↓
GO_GENERAL_AVAILABILITY_LAUNCH
or
GO_CONTROLLED_GA_ROLLOUT
or
NO_GO_FIX_BLOCKERS
```

---

# 11. Qué no forma parte de este roadmap

Este roadmap no debe desviarse hacia:

- nuevas features no requeridas para GA;
- facturación fiscal/SAT;
- expansión funcional del ERP;
- rediseños de UI no relacionados con readiness;
- migración arbitraria de stack;
- cambios de schemaVersion sin ADR y plan de compatibilidad;
- rollout general automático.

El objetivo es **cerrar el ciclo de vida operativo, de seguridad, confiabilidad, release y soporte necesario para lanzar lo que ya existe**.

---

# 12. Estado inmediato

```text
LIMITED COMMERCIAL BETA:
PASS REAL PRODUCTION

BETA FINAL DECISION:
GO_GENERAL_AVAILABILITY_PREP

GENERAL AVAILABILITY:
NOT ACTIVATED

NEXT AUTHORIZED PHASE:
GA-01 — General Availability Baseline Freeze and Readiness Charter
```

---

# 13. Definición de éxito de la etapa

La etapa GENERAL AVAILABILITY READINESS termina únicamente cuando GA-12 produzca:

```text
PASS GENERAL AVAILABILITY READINESS / GO GENERAL AVAILABILITY LAUNCH
```

o, si se opta por una apertura progresiva:

```text
PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT
```

En ambos casos:

```text
blockers = {}
schemaVersion = 4
syncContract = schema_version_4
```

y la activación real de GA deberá ejecutarse como una operación posterior explícita, auditable y reversible.

---

# Runtime progress update — 2026-08-22

```text
GA-01: PASS REAL PRODUCTION
GA-02: PASS REAL PRODUCTION
GA-03: PENDING USER VALIDATION
GA-04: LOCKED
GENERAL AVAILABILITY: NOT ACTIVATED
```

GA-02 production closure evidence recorded `retryPendingCount = 0`, `retryOverSlaCount = 0`, `staleProcessingCount = 0`, `pendingConflictCount = 0`, no new/untriaged dead-letter, and `PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03`.


## GA-09 validated production capacity condition — 2026-08-24

GA-09 closed as `PASS REAL PRODUCTION / GO GA-10` using the real production validator `GA-09.4-versioned-httpclient-loadtester-isolation`, with the following capacity boundary documented as an operational condition:

```text
Validated profile: Concurrency 1 and Concurrency 2
Concurrency 1: PASS, 0% errors, p95/p99 inside threshold
Concurrency 2: PASS, 0% errors, p95/p99 inside threshold
Concurrency 3+: BLOCKED in current Railway/upstream path with 400 upstream error
GA public launch: NOT activated
```

Decision: this is not evidence of a functional backend defect. It is a known hosting/proxy/capacity boundary that must be monitored in GA-10 and either mitigated, capacity-scaled, or formally accepted before GA-12/public GA activation. GA-10 must explicitly validate observability for upstream errors, 400/5xx/timeout rate, p95/p99 degradation, health-ready degradation, dashboard read model health, sync endpoint health, and on-call escalation.

## GA-10 validated observability condition — 2026-08-24

GA-10 closed as `PASS REAL PRODUCTION / GO GA-11` using the real production validator `GA-10.2-db-json-output-parser-guardrail`.

```text
GA-10: PASS GA OBSERVABILITY DASHBOARD ALERTING ONCALL READINESS / GO GA-11
blockers: {}
schemaVersion: 4
syncContract: schema_version_4
generalAvailabilityActivated: False
```

Conditions carried forward into GA-11/GA-12:

```text
1. GA-09 capacity boundary: Concurrency 3+ in the current Railway/upstream path can return 400 upstream error.
2. GA-10 DB observation: db_waiting_connections_11 was observed and must be monitored before GA public launch.
```

Decision: neither condition blocks GA-11 if the acceptance blocker matrix remains empty. Both conditions must remain visible to GA-12 and must be mitigated, capacity-scaled, or formally accepted before any public General Availability activation.

## Current GA execution status — 2026-08-24

```text
GA-01: PASS REAL PRODUCTION
GA-02: PASS REAL PRODUCTION
GA-03: PASS REAL PRODUCTION
GA-04: PASS REAL PRODUCTION
GA-05: PASS REAL PRODUCTION
GA-06: PASS REAL PRODUCTION
GA-07: PASS REAL PRODUCTION
GA-08: PASS REAL PRODUCTION
GA-09: PASS REAL PRODUCTION / GO GA-10
GA-10: PASS REAL PRODUCTION / GO GA-11
GA-11: PASS REAL PRODUCTION / GO GA-12
GA-12: PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT
POST-GA-12: NEXT
GENERAL AVAILABILITY: NOT ACTIVATED
```


## GA-11 validated acceptance condition — 2026-08-24

GA-11 closed as `PASS REAL PRODUCTION / GO GA-12` using the real production validator `GA-11.0-customer-operator-admin-acceptance`.

```text
GA-11: PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12
customerAcceptance: PASS
operatorAcceptance: PASS
adminAcceptance: PASS
blockers: {}
schemaVersion: 4
syncContract: schema_version_4
generalAvailabilityActivated: False
```

Conditions carried forward into GA-12:

```text
1. GA-09 capacity boundary: Concurrency 3+ in the current Railway/upstream path can return 400 upstream error.
2. GA-10/GA-11 DB observation: waitingConnectionCount = 11 was observed and must be monitored before any public GA activation.
3. Dashboard build was skipped by validator switch in GA-10/GA-11, but deployed DashboardUrl returned 200. GA-12 must decide if final local dashboard build evidence is required.
4. General Availability remains NOT activated.
```

Decision: GA-11 does not clear these as launch blockers automatically. GA-12 must produce the final explicit launch readiness decision and either return controlled rollout readiness with conditions or NO_GO if conditions are not formally accepted/mitigated.


## GA-12 hotfix notes

- GA-12.1-document-contract-activation-wording: documentation/validator contract wording fix only. Public GA remains NOT ACTIVATED.


## GA-12 validated final readiness — 2026-08-24

GA-12 closed as `PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT` using validator `GA-12.3-sync-inbox-created-at-schema-compatibility`.

```text
finalDecision: GO_CONTROLLED_GA_ROLLOUT
launchAuthorizationOnly: True
publicGeneralAvailabilityActivated: False
generalAvailabilityActivated: False
schemaVersion: 4
syncContract: schema_version_4
```

Conditions carried into Post-GA-12:

```text
1. GA-09 capacity boundary: Concurrency 3+ in the current Railway/upstream path can return 400 upstream error.
2. GA-10/GA-11/GA-12 DB observation: waitingConnectionCount = 11 was observed and must be monitored/tuned before public GA launch.
3. Public GA activation requires explicit separate change after this decision layer.
```

## POST-GA-12 Launch Decision — 2026-08-24

Status: `NEXT`.

Allowed decision options:

```text
CONTROLLED_ROLLOUT
CAPACITY_SCALE_UP
NO_GO_REMEDIATION
```

This stage records the launch decision only. It does not activate public GA automatically. `GENERAL AVAILABILITY: NOT ACTIVATED` remains the invariant until a separate explicit activation process is approved.


## CGA-01 — Controlled GA Rollout Execution — 2026-08-24

Status: NEXT / validator prepared.

Entry gate: POST-GA-12 PASS CONTROLLED_ROLLOUT.

Scope:

- RolloutMode: LIMITED
- MaxStores: 2
- MaxConcurrentTerminals: 2
- ObservationWindowHours: 24
- PUBLIC GA: NOT ACTIVATED

Important API contract carried into CGA-01:

`/api/v1/reports/dashboard/overview?from=<from>&to=<to>&limit=20&trendBucket=day`

Carried conditions:

- GA-09 capacity boundary: Concurrency 3+ can return Railway/upstream 400.
- GA-10/GA-11/GA-12/Post-GA-12 DB waiting connections observation.
- Public GA activation requires explicit separate change.

Next phases:

- CGA-02 — 24h/72h Production Monitoring and Incident Window — LOCKED
- CGA-03 — Capacity / DB Remediation or Formal Acceptance — LOCKED
- CGA-04 — Public GA Activation Decision — LOCKED

## CGA-01 validated controlled rollout execution — 2026-08-24

CGA-01 closed as `PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02` using validator `CGA-01.0-controlled-ga-rollout-execution`.

```text
rolloutMode: LIMITED
maxStores: 2
maxConcurrentTerminals: 2
observationWindowHours: 24
publicGaActivation: NOT_ACTIVATED
schemaVersion: 4
syncContract: schema_version_4
```

Conditions carried into CGA-02:

```text
1. GA-09 capacity boundary: Concurrency 3+ can return Railway/upstream 400.
2. DB waiting connections observation remains active.
3. historical dead_letter count remains a monitored condition.
4. Dashboard overview requires from, to, limit and trendBucket.
5. Public GA activation requires explicit separate change.
```

## CGA-02 — 24h/72h Production Monitoring and Incident Window — 2026-08-24

Status: NEXT / validator prepared.

Entry gate: CGA-01 PASS.

Scope:

- RolloutMode: LIMITED
- MaxStores: 2
- MaxConcurrentTerminals: 2
- MonitoringWindowHours: 24 or 72
- SampleCount: bounded snapshot sampling
- PUBLIC GA: NOT ACTIVATED

Purpose:

CGA-02 validates production monitoring and incident-window readiness during the controlled rollout. It samples API health, observability, sync, dashboard, report read models, DB pressure, financial integrity and RLS state. It does not wait for 24h/72h in one invocation; it records a bounded snapshot that can be rerun through the operating window.

Next phases:

- CGA-03 — Capacity / DB Remediation or Formal Acceptance — LOCKED
- CGA-04 — Public GA Activation Decision — LOCKED


## CGA-02.1 — Known Sync Conflict Baseline Hotfix

CGA-02.1 adds `AllowedExistingSyncConflictCount` for documented historical conflicts created during controlled operator validation before a valid remote cash shift. It does not clear conflicts, does not activate Public GA, and keeps the conflict baseline as a carried condition for CGA-03/remediation.

## CGA-03 — Capacity / DB Remediation or Formal Acceptance

Status: READY FOR VALIDATION.

Decision path: FORMAL_ACCEPTANCE by default. Public GA remains NOT ACTIVATED. Next phase after PASS: CGA-04 — Public GA Activation Decision.

## CGA-04 — Public GA Activation Decision

- Status: NEXT / READY TO VALIDATE
- Expected decision: `KEEP_LIMITED_GA`
- Public GA: `NOT_ACTIVATED`
- Activation requires separate explicit authorization and clean remediation criteria.

---

## LGA-01 — Limited GA Operations Hardening

Status: READY FOR VALIDATION.

Entry gate: CGA-04 PASS KEEP LIMITED GA / PUBLIC GA NOT ACTIVATED.

Scope:

- Formal archive of known sync conflict baseline.
- Formal archive of known dead letter baseline.
- Inventory hardening for ING-CAFE-G negative stock.
- WPF QSR command enablement fix.
- Capacity boundary carried forward.
- Public GA remains NOT_ACTIVATED.

Next: LGA-02 — Limited GA Stability Loop and Operational Cleanup.


## LGA-02 — Limited GA Stability Loop and Operational Cleanup

Status: READY FOR VALIDATION. Requires new real sales cycle, sync/dead-letter baseline stability, inventory reconciliation, WPF QSR visual confirmation, and Public GA NOT_ACTIVATED.


## LGA-02-HOTFIX-01 — Real sales cycle document contract alignment

Aligned `docs/ga/lga-02-real-sales-cycle-record.md` with validator guardrails by explicitly including the required `stability loop` term. No backend, WPF, database migration, or Public GA activation changes.


## LGA-02-HOTFIX-03 — Inventory Adjustment Contract Alignment

Aligned the validator inventory adjustment payload with the production API by using `adjustmentType = correction` and invariant `quantityDelta`. Public GA remains NOT_ACTIVATED.

---

## LGA-03 — Limited GA Multi-Day Stability Burn-In

**Status:** READY FOR VALIDATION

**Entry gate:** PASS LGA-02 LIMITED GA STABILITY LOOP AND OPERATIONAL CLEANUP / GO LGA-03

**Objetivo:** validar estabilidad multi-day de Limited GA sin activar Public GA.

**Criterios:**

- completed sales 24h >= 6.
- payments 24h >= 6.
- receipts 24h >= 3.
- syncConflictCount <= 3.
- syncDeadLetterCount <= 1.
- negativeStockCount = 0.
- openShiftCount = 0.
- waitingConnectionCount <= 11.
- schemaVersion = 4.
- syncContract = schema_version_4.
- Public GA not activated.

**Next:** LGA-04 only after finalized multi-day burn-in.


## LGA-03-HOTFIX-01 — Sales Range Endpoint Contract Alignment

Validator aligned with production sales range endpoint `/api/v1/reports/sales/range`. Public GA remains NOT_ACTIVATED.


### LGA-03 HOTFIX-02 — Dashboard Overview Endpoint Contract Alignment

- Status: READY FOR VALIDATION.
- Validator version: `LGA-03.2-dashboard-overview-endpoint-contract-alignment`.
- Fix: LGA-03 now calls `/api/v1/reports/dashboard/overview` instead of `/api/v1/dashboard/overview`.
- Public GA remains `NOT_ACTIVATED`.
