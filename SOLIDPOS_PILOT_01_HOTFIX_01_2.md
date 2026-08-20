# SolidPOS PILOT-01 Hotfix 01.2 — PostgreSQL temp table schema qualification

## Status

PENDING USER VALIDATION

## Problem

The PILOT-01 PostgreSQL validation still failed in environments where the session search_path did not resolve the temporary table by short name after setting tenant context.

Observed error:

```text
ERROR: relation "pilot_01_state" does not exist
LINE 1: INSERT INTO pilot_01_state (
```

## Fix

`pilot_01_state` is now fully schema-qualified as `pg_temp.pilot_01_state` in all statements:

- `DROP TABLE IF EXISTS pg_temp.pilot_01_state`
- `CREATE TEMP TABLE pg_temp.pilot_01_state`
- `INSERT INTO pg_temp.pilot_01_state`
- all reads from `pg_temp.pilot_01_state`

## Impact

No product behavior changes. This only hardens the PILOT-01 validation SQL script for PostgreSQL/psql execution.
