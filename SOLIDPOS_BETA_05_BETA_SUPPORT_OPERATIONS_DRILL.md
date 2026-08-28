# SolidPOS BETA-05 — Beta Support Operations Drill

## Objective
Validate production-beta support operations with real tenant evidence, established SEV/runbook contracts, safe triage decisions, customer communication and a reproducible support evidence package.

## Modules affected
- `scripts/beta`
- `docs/beta`
- inherited `scripts/expansion/validate-exp-08-support-incident-operations.ps1` (reused, not duplicated)

## Technical decisions
- Reuse EXP-08 as the hardened support/incident contract.
- Do not automatically retry, delete dead-letter evidence, force-close cash shifts or perform rollback during a validation drill.
- Historical operational conditions remain non-blocking only when evidence is complete and no pending conflict/stale processing blocker exists.
- SQL source-of-truth uses `audit_events.occurred_at` and tenant UUID cast rules already established.

## Risks
- Retry without correcting the root cause can amplify duplicate work.
- Force-closing a shift without operator evidence can corrupt cash accountability.
- Removing dead-letter evidence destroys forensic context.
- Rollback without explicit trigger/evidence can increase impact.

## Files added
- `scripts/beta/validate-beta-05-support-operations-drill.ps1`
- `scripts/beta/beta-05-support-operations-drill-check.sql`
- `docs/beta/beta-05-support-operations-drill.md`
- `docs/beta/beta-05-incident-intake-and-resolution-checklist.md`
- `docs/beta/beta-05-customer-communication-template.md`
- `docs/beta/beta-05-go-no-go.md`
- `docs/beta/logs/beta-05-support-operations-drill-log.md`
- `BETA_05_VALIDATION_COMMANDS.md`

## Runtime evidence
- `.runtime/beta-05-support-operations-drill/beta-05-support-evidence-package.json`
- `.runtime/beta-05-support-operations-drill/beta-05-support-operations-manifest.json`

## Expected decision
`PASS BETA SUPPORT OPERATIONS DRILL / GO BETA-06`

## Delivery status
`PENDING USER VALIDATION`
