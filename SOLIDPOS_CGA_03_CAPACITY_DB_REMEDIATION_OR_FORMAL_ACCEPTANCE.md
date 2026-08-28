# SolidPOS CGA-03 — Capacity / DB Remediation or Formal Acceptance

## Summary

CGA-03 is a controlled-GA decision gate. It does not expand rollout and does not activate Public GA.

The current recommended path is `FORMAL_ACCEPTANCE` of the limited capacity envelope:

- max 2 stores;
- max 2 concurrent terminals/open shifts;
- known sync conflict baseline allowed at 3;
- known dead letter baseline allowed at 1;
- known waiting DB connection baseline allowed at 11;
- Public GA remains NOT ACTIVATED.

## Files added

- `scripts/ga/validate-cga-03-capacity-db-remediation-or-formal-acceptance.ps1`
- `scripts/ga/cga-03-capacity-db-remediation-or-formal-acceptance-check.sql`
- `docs/ga/cga-03-capacity-db-remediation-or-formal-acceptance.md`
- `docs/ga/cga-03-capacity-decision-record.md`
- `docs/ga/cga-03-evidence-matrix.md`
- `docs/ga/cga-03-go-no-go.md`
- `CGA_03_VALIDATION_COMMANDS.md`

## Files not changed

- PosServer application code.
- PosCore application code.
- PosDashboard application code.
- Database migrations.
- C# contracts.

## Next phase

CGA-04 — Public GA Activation Decision.


## HOTFIX-01 / CGA-03.1

The CGA-03.1 validator keeps the same formal acceptance decision but adds observability metric compatibility: if `/api/v1/observability/metrics` does not expose a top-level `p95LatencyMs`, the manifest records a p95 fallback from the explicit capacity probes (`health-ready` and `dashboard-overview`) instead of failing after the blocker matrix has passed. This is a validator compatibility fix only; backend, database schema, contracts and Public GA activation are unchanged.
