# SolidPOS BETA-01 — Controlled Commercial Beta Onboarding

## Status
`PENDING USER VALIDATION`

## What changed
BETA-01 adds the production validator, SQL source-of-truth cross-check, onboarding documentation, acceptance gates, support contact matrix, GO/NO-GO contract and runtime evidence manifest required to onboard the first controlled commercial beta customer.

## Modules affected
- `scripts/beta`
- `docs/beta`
- repository-level validation documentation

No PosServer domain/API schema or database migration was changed in this phase. BETA-01 intentionally validates the already-proven EXP-12 production contracts before broadening tenancy in BETA-02.

## Technical decisions
1. `schemaVersion = 4` remains mandatory.
2. SQL is source of truth for customer/admin/catalog/pricing/release evidence when list/runtime visibility is partial.
3. The login email must resolve to exactly one active, unlocked tenant user.
4. The admin requires role assignment and active-store access.
5. At least one active customer profile, active store, terminal assigned to active store, sellable product, price list and current MXN price are required.
6. `beta` update channel availability is verified through the protected API; tenant release and universal Velopack evidence are cross-checked in SQL.
7. Existing retry/dead-letter records remain non-blocking only when documented as monitored conditions; pending conflicts and legacy schema events are blockers.
8. No secrets are written to the manifest or logs.

## Risks
- Runtime customer/catalog list visibility may be partial even when SQL is correct; this is recorded as a condition rather than falsifying the source of truth.
- Existing retry/dead-letter conditions require continued triage during beta.
- Production validation depends on Docker, PostgreSQL access, .NET SDK, valid admin credentials and production availability.

## Files added
- `scripts/beta/validate-beta-01-controlled-commercial-beta-onboarding.ps1`
- `scripts/beta/beta-01-controlled-commercial-beta-onboarding-check.sql`
- `BETA_01_VALIDATION_COMMANDS.md`
- `SOLIDPOS_BETA_01_CONTROLLED_COMMERCIAL_BETA_ONBOARDING.md`
- `docs/beta/beta-01-controlled-commercial-beta-onboarding.md`
- `docs/beta/beta-01-store-onboarding-checklist.md`
- `docs/beta/beta-01-support-contact-matrix.md`
- `docs/beta/beta-01-customer-acceptance-checklist.md`
- `docs/beta/beta-01-go-no-go.md`
- `docs/beta/logs/beta-01-controlled-commercial-beta-onboarding-log.md`

## Expected production decision
`PASS CONTROLLED COMMERCIAL BETA ONBOARDING / GO BETA-02`

BETA-02 must not begin until the user validates this phase successfully in real production.
