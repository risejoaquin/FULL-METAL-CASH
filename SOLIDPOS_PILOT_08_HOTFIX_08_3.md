# SolidPOS PILOT-08 HOTFIX 08.3

## Status

PENDING USER VALIDATION

## Scope

Fixes the isolated restore bootstrap in `scripts/pilot/validate-backup-restore-rollback-drill.ps1`.

## Failure addressed

PILOT-08 HOTFIX 08.2 reached the restore container, but the extension bootstrap failed with:

```text
psql: error: connection to server on socket "/var/run/postgresql/.s.PGSQL.5432" failed: No such file or directory
Restore extension bootstrap failed.
```

## Root cause

The restore bootstrap used `docker exec ... psql` without an explicit host. In the isolated Docker restore path this can resolve to the Unix socket before the server socket is available. The validation should connect to the restore container PostgreSQL over TCP with explicit credentials.

## Changes

- `Wait-PostgresContainerReady` now checks readiness with `pg_isready -h 127.0.0.1 -p 5432`.
- Added a real `SELECT 1` readiness check after `pg_isready`.
- Added restore helper wrappers:
  - `Invoke-RestorePsql`
  - `Invoke-RestorePsqlOutput`
- Restore bootstrap, schema restore, and restore validation now use:
  - `docker exec -e PGPASSWORD=solidpos`
  - `psql -h 127.0.0.1 -p 5432 -U postgres -d solidpos_restore`

## Expected next result

```text
[PILOT-08] Restore backup into isolated PostgreSQL container PASS
[PILOT-08] Restore schema validation PASS
[PILOT-08] Production rollback transaction drill PASS
[PILOT-08] PILOT-08 PASS REAL PRODUCTION / GO
```

## Safety

No backend, Dashboard, PosCore, migration, seed, or production data changes.
