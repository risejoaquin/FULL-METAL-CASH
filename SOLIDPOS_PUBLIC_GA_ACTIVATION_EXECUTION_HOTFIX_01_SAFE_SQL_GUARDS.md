# SolidPOS Public GA Activation Execution Hotfix 01

## Cause
The activation SQL used `1/0` inside `CASE` expressions as an assertion mechanism. PostgreSQL can evaluate/fold the constant expression and raise `division by zero` even when the logical branch is intended to be safe.

## Fix
- Replaced arithmetic assertion traps with native `psql` guards using `\gset`, `\if`, and `\quit`.
- Added explicit transaction rollback when persisted activation state does not match the required Public GA state.
- Applied the same safe guard pattern to rollback SQL.
- No readiness thresholds, schema version, sync contract, sales/inventory behavior, RBAC, or API contracts changed.

## Safety
Activation still requires:
- `-ExecuteActivation`
- `-ConfirmationPhrase ACTIVATE_PUBLIC_GA`

Rollback still requires:
- `ROLLBACK_PUBLIC_GA`

The validator remains fail-closed.
