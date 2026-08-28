# GA-03 — SLO / SLI / Error Budget Contract

This contract fixes the initial GA operating objectives. These are explicit readiness targets, not a claim that historical telemetry exists for every rolling window. GA-09 and GA-10 will validate performance capacity and production alerting/telemetry depth.

| SLI | GA-03 SLO / threshold | Window | Owner | Error budget / breach action |
|---|---:|---|---|---|
| API availability | >= 99.9% | rolling 30 days | Platform On-call | 0.1% monthly error budget; SEV1 if readiness/database unavailable, SEV2 for sustained degraded availability |
| API p95 latency | <= 5000 ms | rolling 15 minutes | Backend Owner | breach consumes performance budget; SEV2 and latency triage |
| failed request rate | < 1.0% | rolling 15 minutes | Platform On-call | >=1% is SEV2; any new failed request during a readiness validation is investigated |
| sync processing delay | <= 15 minutes | per event | Sync Owner | >15 minutes is SEV2; stale processing is blocker |
| retry backlog age | <= 15 minutes | oldest executable retry | Sync Owner / Support Lead | >15 minutes is SEV2; executable GA backlog must be zero for readiness |
| dead-letter creation | 0 new | rolling 24 hours | Support Lead / Sync Owner | any new dead-letter is SEV2 and requires triage; retained historical evidence is not executable work |
| payment failure rate | 0% | rolling 24 hours | Payments Owner | any failed production payment is SEV1 until payment integrity is established |
| data reconciliation failures | 0 | rolling 24 hours / latest reconciliation | Operations Owner | any unexplained sales/cash/inventory reconciliation failure is SEV1 or SEV2 by integrity impact |

## Threshold provenance

- `p95 <= 5000 ms` inherits the EXP-05 operational monitoring threshold.
- sync processing and retry age `<= 15 minutes` inherit EXP-07.
- zero new dead-letter, zero failed payments and zero reconciliation failures preserve the beta/GA blocker semantics already used by the production validators.
- `API availability >= 99.9% / 30d` and `failed request rate < 1% / 15m` are explicit initial GA targets introduced by GA-03. They are contract decisions, not silently inferred measurements.

## Error-budget policy

1. Error budget is a release-control signal, not permission to ignore incidents.
2. SEV1 data-integrity, tenant-isolation, payment-integrity, or database-readiness incidents override remaining error budget.
3. When availability or failed-request budget is exhausted, stable promotion pauses until the owner documents recovery and the next GA gate revalidates the affected domain.
4. No General Availability activation occurs in GA-03.
