# HOTFIX GA-08.1 — Mature Production Migration Fast-Forward

## Root cause
`apply-postgresql-migrations.ps1` treated any existing `pos` schema as requiring a replay of migrations 002-020. On a production database already evolved through GA-06/019, migration 006 attempted to restore an older sync status constraint and conflicted with the valid later `dead_letter` state.

## Changes
- Detect `pos.update_release_targets` as the GA-06/019 migration marker.
- When the marker exists, skip historical migrations 002-019 and evaluate GA-08/020 only.
- If tenant RLS coverage is already complete, report no pending migrations.
- Harden migration 006 so the historical status constraint also accepts `dead_letter` for legitimate legacy upgrade paths.

## Safety
- No `ResetSchema`.
- No destructive data mutation.
- No API/runtime behavior changes.
- No GA activation.
