# SolidPOS Public GA Stability Burn-In — Hotfix 01

## Reason

The original burn-in treated every PostgreSQL `pg_stat_activity.wait_event IS NOT NULL` row as database pressure. PostgreSQL also reports normal idle client sessions waiting on `ClientRead`, so the metric could exceed the operational threshold even when the server was healthy.

## Correction

The blocker metric now counts only **active server-side waits**: active sessions with a wait event whose `wait_event_type` is neither `Client` nor `Activity`. The historical broad wait-event count is retained as diagnostics together with client waits, idle sessions, idle-in-transaction sessions and total connections.

No Public GA flags, capacity thresholds, schema version, sync contract, financial rules, RLS rules or inventory rules are changed.

Validator version: `PUBLIC-GA-STABILITY-BURN-IN.1.1-server-wait-pressure-semantics`.
