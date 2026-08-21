# SolidPOS BETA-06 — Beta Release Promotion and Rollback Drill

## Status
PENDING USER VALIDATION.

## Objective
Validate controlled `internal -> beta` release promotion and a rollback drill without persistent destructive mutation.

## Contract
- `/api/v1/updates/channels`
- `POST /api/v1/updates/releases`
- `/api/v1/updates/check`
- tenant-scoped release
- non-mandatory beta update
- Velopack universal installer
- artifact hash and signature preserved during promotion
- rollback version present
- SQL release cross-check
- transactional rollback drill that restores `revoked_at` by `ROLLBACK`

## Decision
`PASS BETA RELEASE PROMOTION ROLLBACK DRILL / GO BETA-07`
