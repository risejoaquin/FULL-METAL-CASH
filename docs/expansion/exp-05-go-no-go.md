# EXP-05 GO/NO-GO

## GO

EXP-05 es GO cuando:

- build PASS.
- tests PASS.
- secret scan PASS.
- health/live alive.
- health/ready ready.
- database ready.
- monitoring endpoints PASS.
- SQL operational monitoring cross-check PASS.
- blockers = empty.
- conditions tienen owner/action.

## NO-GO blockers

- liveness_not_alive.
- readiness_not_ready.
- database_not_ready.
- required_table_missing.
- pending_conflicts.
- failed_payments_last_24h.
- failed_requests.
- p95_latency_over_threshold.
- cash_shift_difference_last_24h.

## Conditions permitidas

- monitor_retry_pending_sync.
- triage_known_dead_letter.
- inventory_reconciliation_required.
- review_open_cash_shifts.

## Siguiente fase

Si EXP-05 es GO, avanzar a EXP-06 — Inventory Reconciliation Hardening.
