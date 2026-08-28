# SolidPOS PILOT-01 Hotfix 01.3 — PostgreSQL validation without temp table / DO block

## Status
Prepared for user validation.

## Problem
The previous SQL validation used psql variables together with a temporary table and a PL/pgSQL DO block. In the user's production validation context, PostgreSQL/psql resolved the temporary table inconsistently after `SET search_path TO pos, public`, causing:

```text
ERROR: relation "pg_temp.pilot_01_state" does not exist
```

## Fix
Replaced the temp-table/DO-block assertion with pure SQL CTEs:

- `params` CTE receives psql variables.
- `facts` CTE computes controlled-store readiness counters.
- `verdict` CTE computes GO/NO-GO.
- Final assertion returns PASS when GO and intentionally exits non-zero on NO-GO.

No application code, database schema, or production data are changed.
