# SolidPOS — HOTFIX GA-06.4

## GA-06 scalar empty-result null safety

Status: PENDING USER VALIDATION

### Failure
GA-06 reached the production cohort-targeting migration preflight successfully, then failed while selecting a controlled cohort with `InvokeMethodOnNull`.

### Root cause
`Invoke-DbScalar` removed whitespace-only output before selecting the last row. A successful PostgreSQL scalar query whose canonical value is the empty string therefore produced `$null`, and the helper invoked `.Trim()` on that null value.

The GA-06 query intentionally returns an empty string when there is no pre-existing release target. Empty is a valid state and means the validator should continue to select a fresh controlled validation terminal.

### Fix
`Invoke-DbScalar` now:
- preserves successful scalar output as strings;
- returns `''` when psql produced no scalar text;
- never invokes `.Trim()` on `$null`;
- still fails closed when the native psql command exits non-zero.

### Safety
No production data mutation is introduced by this hotfix. No release is created, revoked, promoted or deleted by the fix itself. Cohort targeting, migration 019, schema version 4, `schema_version_4`, and `generalAvailabilityActivated = False` remain unchanged.
