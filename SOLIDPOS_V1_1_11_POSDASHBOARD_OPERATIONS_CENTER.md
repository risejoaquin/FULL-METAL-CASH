# SolidPOS V1.1-11 — PosDashboard Operations Center

## Technical Specification & Operational Overview

- **Phase:** V1.1-11 PosDashboard Operations Center
- **Baseline SHA:** `5557add2562759ff429a0df2c4fd0785ea1d1b48`
- **Branch:** `lucilferChanges/fastandrun`
- **Historical Lockfile Source:** `322fef5c222c7fa536c7f1faea8f22209e552eeb`
- **schemaVersion:** 4
- schemaVersion: 4
- **syncContract:** schema_version_4
- syncContract: schema_version_4
- **Database Migration Required:** NO (0 migrations)
- **PosServer Mutation Required:** NO (0 backend mutations)

---

## 1. Scope & Architectural Boundaries

In accordance with `SOLIDPOS_PRODUCT_DEVELOPMENT_ROADMAP_POST_V1_20260828.md`:

```markdown
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
```

### Architectural Rules
1. **PosDashboard is a Pure Consumer:** PosServer remains the authoritative security, tenancy, store, and observability boundary. The dashboard does not scrape raw Prometheus metrics, read raw database tables, or access internal filesystem logs.
2. **Safe Model Consumption:** Only sanitized public and authenticated REST endpoints are consumed:
   - `/health/ready` (API & DB readiness, schema compatibility, sync readiness, storage readiness, database latency)
   - `/api/v1/observability/metrics` (Operational request rates, database connection counts, sync metrics, sales/payment latencies)
   - `/api/v1/observability/alerts` (Active production alert evaluations with observed vs threshold metrics)
   - `/api/v1/sync/status` (Runtime sync queue states, pending/processing/dead-letter breakdown, oldest pending event timestamp)
   - `/api/v1/terminals` (Enrolled terminal list, status, assigned store, application version, last-seen timestamp)
3. **RBAC by Permissions:** The client checks user permissions (`reports.read`, `terminals.manage`, `sync.conflicts.read`, `audit.read`) present in the JWT session. When a user lacks permission for a specific subsystem (e.g. `terminals.manage`), the UI gracefully displays a safe restricted notice without crashing or exposing unauthorized actions. PosServer enforces authoritative 401/403 authorization.
4. **Partial Failure Isolation:** `getOperationsSnapshot()` utilizes `Promise.allSettled()` across all operational endpoints. If a single endpoint returns 403 Forbidden or fails due to network latency, the remaining six operational surfaces continue rendering nominal data.
5. **Deterministic Dependencies:** `package-lock.json` was restored from historical commit `322fef5c222c7fa536c7f1faea8f22209e552eeb` (which matches `package.json` v0.22.0 byte-for-byte), enabling deterministic `npm ci` and hermetic CI builds without dependency drift.

---

## 2. Seven Core Operational Capabilities

### 1. API Health
- Surfaces granular status from `/health/ready`: overall readiness status (`ready` / `unavailable`), database latency in milliseconds, authoritative `schemaVersion` (4), `syncContract` (`schema_version_4`), schema compatibility (`compatible`), and storage readiness.

### 2. DB Health
- Displays PostgreSQL central database health: database readiness state, active connection count, missing required tables count, and server version.

### 3. Sync Queues
- Full queue lifecycle monitoring from `/api/v1/sync/status`:
  - `Pending`, `Processing`, `Processed`, `RetryPending`, `DeadLetter`, `Duplicate`, `Rejected`
  - `OldestPendingAt`: timestamp of the oldest pending sync event in the inbox
  - `LastProcessedAt`: timestamp of the last processed event
  - Visual status tone transitions to `recovery req` if dead-letter events exist.

### 4. Terminal Status
- Fleet monitoring from `/api/v1/terminals`:
  - Enrolled terminal list with terminal name, status (`Active` / `Disabled` / `Revoked`), application version, and last-seen timestamp.
  - Safe empty state when no terminals are enrolled or when restricted.
  - RBAC warning displayed when user lacks `terminals.manage`.

### 5. Incidents
- Read-only operational incident summary derived from live telemetry:
  - Critical active alerts (e.g. `sync_dead_letter`, `postgres_idle_in_transaction`, `negative_stock`, `sale_payment_mismatch`)
  - Dead-letter queue presence requiring administrator recovery
  - Sync queue backlog warnings (>50 pending events)
  - Displays nominal `CLEAR` badge when no incidents are active.

### 6. Version Adoption
- Fleet version distribution calculated from terminal telemetry:
  - Server version / schema version
  - Total enrolled terminal count
  - Version breakdown per application version (`count` and `percentage`)
  - Unknown/unreported version tally

### 7. Alert Summary
- Evaluated production alert rules from `/api/v1/observability/alerts`:
  - Alert code (e.g. `http_error_ratio`, `http_p95_latency_ms`, `postgres_active_non_client_wait_event`, `sync_dead_letter`, `negative_stock`)
  - Severity badge (`critical` / `warning`)
  - Status indicator (`ACTIVE` / `OK`)
  - Observed value vs approved threshold and operator description
  - Overall status badge (`ALL NOMINAL` vs `ALERTS ACTIVE`)
