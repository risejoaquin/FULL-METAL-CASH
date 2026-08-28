# EXP-09 — Release Management and Update Channel

## Status
PENDING USER VALIDATION.

## Objective
Formalize SolidPOS release management and update channel operations after EXP-08. This phase verifies that limited production expansion can receive controlled updates through a repeatable release process.

## Scope
- SemVer release naming.
- Candidate/internal and stable channel policy.
- Tenant-scoped update release creation.
- Non-mandatory Velopack update package contract.
- Release smoke test using `/api/v1/updates/channels`, `/api/v1/updates/releases`, and `/api/v1/updates/check`.
- Safe migration preflight policy.
- Rollback version contract.
- Release notes and GO/NO-GO evidence.

## Production mutation
EXP-09 creates one tenant-scoped, non-mandatory update release in the `internal` channel. It does not modify sales, payments, cash shifts, inventory, stores, terminals, sync events, or tenant identity data.

## Gate
EXP-09 is GO only when build, tests, health/readiness, admin login, release channel contract, update release creation, update check, SQL cross-check, release notes, migration policy, rollback policy, smoke test, and GO/NO-GO are PASS.

## Next phase
EXP-10 — Customer/Admin Management Completion.
