# HOTFIX GA-06.3 — Sync Inbox Status Constraint Forward Compatibility

## Root cause

The migration runner intentionally reapplies idempotent migrations. `006_sync_processing_runtime.sql` recreated `sync_inbox_events_status_check` with the older status set:

`received | processing | processed | duplicate | rejected | conflict`

Later migration `013_sync_conflict_resolution_runtime.sql` legitimately expanded the runtime contract with:

`retry_pending | dead_letter`

Production already contains historical rows using the newer states. Re-running migration 006 against that valid newer state therefore failed before migration 013 could reassert the expanded constraint.

## Correction

Migration 006 now uses the current forward-compatible status superset:

`received | processing | processed | duplicate | rejected | retry_pending | conflict | dead_letter`

Migration 013 already uses exactly the same set, so the chain is now idempotent when replayed over a database that has progressed through later sync phases.

## Safety

- No UPDATE or DELETE of sync history.
- No rewriting `dead_letter` or `retry_pending` rows.
- No schemaVersion change.
- No sync contract change (`schema_version_4`).
- No GA activation.
- No weakening of the status constraint beyond states already supported by current runtime code and migration 013.
