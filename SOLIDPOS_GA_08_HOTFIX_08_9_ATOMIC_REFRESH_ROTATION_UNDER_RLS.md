# SolidPOS GA-08 Hotfix 08.9 — Atomic Refresh Rotation Under RLS

## Cause
GA-08.8 proved the validator and tenant-scoped payload were active, but production still returned `invalid-refresh-token` during `/api/v1/auth/refresh`. The remaining risk was a split read-then-update refresh flow under RLS: lookup, role loading, and token rotation were separate repository calls.

## Fix
Refresh rotation is now a single tenant-scoped PostgreSQL statement that updates the old token, inserts the replacement token, links `replaced_by_token_id`, and returns the authenticated user in one atomic operation after setting `app.tenant_id`.

## Scope
- Backend auth repository and service only.
- GA-08 validator version marker updated.
- No schema change.
- No sync contract change.

## Invariants
- schemaVersion = 4
- syncContract = schema_version_4
- RLS remains enforced
- refresh reuse remains rejected
- logout revocation remains required
