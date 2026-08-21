# EXP-09 — Release Rollback Runbook

## Rollback objective
Rollback restores operation to the previous approved version without destructive data changes.

## Required rollback evidence
- Current release version.
- Rollback version.
- Artifact hash and signature.
- Channel.
- Tenant scope.
- Health/readiness status.
- Smoke test status.
- Support owner.
- Incident ID if rollback is caused by production failure.

## Rollback steps
1. Stop promotion to stable.
2. Mark release as candidate/internal until investigation closes.
3. Use rollback version for client update policy.
4. Validate health/readiness and update check.
5. Attach logs, manifest, SQL evidence, and support bitacora.

## No destructive delete
Rollback must not delete production sales, payments, cash shifts, inventory ledger, stores, terminals, sync events, or audit evidence.
