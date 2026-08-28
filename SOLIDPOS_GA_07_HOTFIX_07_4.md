# HOTFIX GA-07.4 — Single-Result Transactional Rollback Evidence

## Cause
The GA-07 rollback SQL emitted two independent JSON documents. The hardened DB JSON helper intentionally returns the last valid JSON result, so the intra-transaction fields (`rollbackValidation`, `rollbackTargetAvailable`) were not present in the object consumed by the validator even though the rollback target existed.

## Fix
- Preserve intra-transaction booleans in psql client variables with `\\gset` before `ROLLBACK`.
- Emit exactly one JSON evidence object after `ROLLBACK` containing both intra-transaction and post-rollback state.
- Keep the stable revoke fully transactional; no persistent release mutation is introduced.
- No database migration, API change, backend deploy, or production data rewrite.

## Expected gate
`[GA-07] Stable application / Velopack rollback transaction drill PASS`

## Safety
- `persistedRollbackMutationCount = 0` remains mandatory.
- stable must be active again after rollback.
- rollback target must already exist and be valid; the hotfix does not create one.
- schemaVersion remains 4; General Availability remains inactive.
