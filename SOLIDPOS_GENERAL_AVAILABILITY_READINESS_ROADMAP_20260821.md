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
