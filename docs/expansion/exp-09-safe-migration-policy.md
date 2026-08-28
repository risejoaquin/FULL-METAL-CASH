# EXP-09 — Safe Migration Policy

## Migration preflight
Before any release is promoted, migrations require:

- `dotnet restore` PASS.
- `dotnet build` PASS.
- `dotnet test` PASS.
- PostgreSQL migration smoke test PASS.
- Backup/restore rollback evidence available from pilot/expansion operations.
- No destructive migration without explicit rollback plan.

## Destructive changes
Destructive changes are NO-GO during limited expansion unless there is a signed incident decision and a tested rollback.

## Release coupling
A release cannot be promoted to stable unless migration preflight and rollback evidence are attached to the release notes.
