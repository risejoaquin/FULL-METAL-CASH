# SolidPOS — HOTFIX GA-06.2

## Status
PENDING USER VALIDATION

## Defect
`apply-postgresql-migrations.ps1` could silently migrate the local Docker database even when a production `DatabaseUrl` had been supplied, if `psql.exe` was not installed locally.

## Resolution
The migration runner now preserves destination intent:
1. If `DatabaseUrl` is non-empty and local `psql` exists, use local `psql` against that URL.
2. If `DatabaseUrl` is non-empty and local `psql` is absent, launch `postgres:17` as a disposable client and connect to that exact URL.
3. Only if `DatabaseUrl` is empty may the script fall back to `docker exec` against `solidpos-postgres`.

No schema contract, GA-06 targeting behavior, release semantics, or production data is otherwise changed.
