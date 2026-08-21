# SolidPOS EXP-05 — Operational Monitoring Hardening

## Status

PENDING USER VALIDATION

## Purpose

Endurecer monitoreo operacional después de EXP-04 para que la expansión limitada tenga métricas críticas con owner, threshold, action, evidence, escalation y GO/NO-GO.

## Changed files

```text
scripts/expansion/validate-exp-05-operational-monitoring-hardening.ps1
scripts/expansion/exp-05-operational-monitoring-hardening-check.sql
EXP_05_VALIDATION_COMMANDS.md
SOLIDPOS_EXP_05_OPERATIONAL_MONITORING_HARDENING.md
docs/expansion/exp-05-operational-monitoring-hardening.md
docs/expansion/exp-05-monitoring-owner-threshold-matrix.md
docs/expansion/exp-05-daily-dashboard-checklist.md
docs/expansion/exp-05-alert-response-runbook.md
docs/expansion/exp-05-evidence-and-escalation.md
docs/expansion/exp-05-go-no-go.md
docs/expansion/logs/exp-05-operational-monitoring-hardening-log.md
```

## Not changed

```text
backend
PosCore
Dashboard UI
PosBuilder
migrations
production seed
production data
```

## Validation gates

- Local repository guardrails.
- EXP-05 document contract.
- Local secret scan.
- dotnet restore.
- dotnet build.
- dotnet test.
- Dashboard build/self-test unless skipped.
- Production liveness/readiness.
- Admin login and monitoring endpoint contract.
- SQL operational monitoring cross-check.
- Operational monitoring threshold matrix.
- Manifest and log generation.

## SQL contract audit

EXP-05 avoids fragile assumptions detected during EXP-03:

```text
NO pos.inventory_current
NO pos.inventory_ledger.sale_id
NO uuid = text without explicit cast
```

Inventory is derived from:

```text
pos.inventory_ledger.reference_type
pos.inventory_ledger.reference_id::text
sum(pos.inventory_ledger.quantity_delta)
```

## Expected result

```text
[EXP-05] EXP-05 PASS OPERATIONAL MONITORING HARDENING / GO EXP-06
```

## Next phase

EXP-06 — Inventory Reconciliation Hardening.


## HOTFIX 05.1

Corrected SQL operational monitoring cross-check to use the real audit event timestamp contract: `pos.audit_events.occurred_at` instead of non-existent `pos.audit_events.created_at`.

The EXP-05 SQL was re-audited for prior fragile assumptions from EXP-03: no `pos.inventory_current`, no `pos.inventory_ledger.sale_id`, no `audit_events.created_at`, and no UUID/text comparison assumptions in expansion evidence checks.
