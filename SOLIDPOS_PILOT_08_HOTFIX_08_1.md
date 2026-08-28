# SolidPOS PILOT-08 HOTFIX 08.1 - PostgreSQL 17 pg_dump Client Compatibility

## Status

PENDING USER VALIDATION

## Context

PILOT-08 reached the schema-only backup step and failed because the production PostgreSQL server is version 17.6 while the Docker client image used by the validator was postgres:16.

Observed failure:

```text
pg_dump: error: aborting because of server version mismatch
pg_dump: detail: server version: 17.6; pg_dump version: 16.15
```

## Root cause

The validator pinned PostgreSQL tooling to `postgres:16`. PostgreSQL `pg_dump` must be the same major version or newer than the server. A PostgreSQL 16 `pg_dump` cannot dump a PostgreSQL 17 server.

## Change

Updated PILOT-08 validator tooling from:

```text
postgres:16
```

to:

```text
postgres:17
```

Affected file:

```text
scripts/pilot/validate-backup-restore-rollback-drill.ps1
```

Docs updated:

```text
PILOT_08_VALIDATION_COMMANDS.md
SOLIDPOS_PILOT_08_BACKUP_RESTORE_ROLLBACK_DRILL.md
```

## Scope

No backend changes.
No PosCore changes.
No Dashboard changes.
No production data mutation.
No migrations.

## Expected next result

The validator should pass:

```text
[PILOT-08] Create logical schema backup with pg_dump PASS
```

and continue to isolated restore validation.

## Notes

The restore container also uses PostgreSQL 17, keeping backup and restore major versions aligned.
