# SolidPOS EXP-05 — Operational Monitoring Hardening

## Estado objetivo

EXP-05 endurece el monitoreo operacional después de EXP-03 y EXP-04. El objetivo no es crear nuevas ventas, tiendas o terminales; el objetivo es que la operación real pueda observarse diariamente con owner, threshold, action, evidence y GO/NO-GO.

## Alcance

- health/live y health/ready.
- database readiness y required tables.
- sync processed, retry_pending, dead_letter y pending conflicts.
- failed payments de las últimas 24 horas.
- failed requests y p95 latency.
- negative inventory.
- cash shift differences.
- open cash shifts.
- audit events.
- store count y terminal count después de segunda terminal / segunda tienda.

## Owner / threshold / action

Cada métrica crítica debe tener:

- Owner operacional.
- Threshold de GO, condición o NO-GO.
- Action inmediata.
- Evidence mínima.
- Escalation path.

## Decisión

EXP-05 permite avanzar a EXP-06 si:

- no hay blockers de salud, base de datos, conflictos pendientes, failed payments o failed requests;
- las condiciones conocidas quedan explícitamente monitoreadas;
- el contrato SQL y el contrato HTTP de observability/metrics pasan.

## Condiciones aceptadas

Las siguientes condiciones pueden seguir vivas sin bloquear si tienen monitoreo y acción:

- monitor_retry_pending_sync.
- triage_known_dead_letter.
- inventory_reconciliation_required.
- review_open_cash_shifts.

## Siguiente fase

EXP-06 — Inventory Reconciliation Hardening.
