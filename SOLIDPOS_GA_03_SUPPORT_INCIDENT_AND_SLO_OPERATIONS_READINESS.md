# SolidPOS — GA-03 Support, Incident and SLO Operations Readiness

## Delivery state
`PASS REAL PRODUCTION`

## Contract
GA-03 requires SEV1–SEV4, incident intake, escalation policy, on-call ownership, explicit rollback authority, sync/cash/payments/release incident runbooks, daily support, audit evidence, post-incident review, explicit SLO/SLI/error-budget thresholds and a fresh GA-02 production PASS.

## Explicit SLO thresholds
- API availability >= 99.9% rolling 30 days.
- API p95 latency <= 5000 ms rolling 15 minutes.
- failed request rate < 1.0% rolling 15 minutes.
- sync processing delay <= 15 minutes.
- retry backlog age <= 15 minutes.
- new dead-letter creation = 0 rolling 24 hours.
- payment failure rate = 0% rolling 24 hours.
- data reconciliation failures = 0.

The availability and failed-request-rate values are explicit initial GA policy decisions, not silent historical measurements. p95 and sync/retry thresholds inherit prior production contracts.

## Invariants
- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `generalAvailabilityActivated = False`
- no destructive production cleanup
- retained historical dead-letter remains evidence, not executable work

## Expected production result
`PASS GA SUPPORT INCIDENT SLO READINESS / GO GA-04`
