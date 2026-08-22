# SolidPOS BETA-07 — Beta Dashboard and Daily Monitoring Pack

## Objective
Validate that the commercial beta has a repeatable dashboard and daily monitoring workflow grounded in production APIs and PostgreSQL source-of-truth.

## Scope
BETA-07 validates dashboard source contracts, health/readiness, operational metrics, sync, dead-letter, conflicts, sales, payments, cash shifts, inventory, audit evidence, and generates a daily monitoring pack plus machine-readable snapshot.

## Decisions
- Reuse EXP-05 operational monitoring rather than create parallel monitoring logic.
- Validate dashboard source even when `-SkipDashboardBuild` is explicitly used.
- Treat known retry/dead-letter/open-shift/low-stock states as conditions when they do not indicate newly unsafe operation.
- Treat pending conflicts, stale processing, failed payments, cash differences, missing audit evidence, or legacy sync schema as blockers.
- Preserve `schemaVersion = 4` and `syncContract = schema_version_4`.

## Expected close
`PASS BETA DASHBOARD DAILY MONITORING PACK / GO BETA-08`

## Validation status
PASS REAL PRODUCTION — PASS BETA DASHBOARD DAILY MONITORING PACK / GO BETA-08.
