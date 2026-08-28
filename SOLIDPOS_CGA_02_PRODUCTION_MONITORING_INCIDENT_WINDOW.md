# SolidPOS CGA-02 — Production Monitoring and Incident Window

## Phase

CGA-02 — 24h/72h Production Monitoring and Incident Window

## Entry gate

CGA-01 PASS REAL PRODUCTION / GO CGA-02.

## Purpose

Convert controlled rollout execution into a monitored production incident window. This validator samples API, observability, sync, dashboard, reports, and DB pressure, then writes a manifest and evidence snapshot.

## Does not activate

This phase does not activate Public GA.

- `publicGeneralAvailabilityActivated=False`
- `generalAvailabilityActivated=False`

## Known conditions

- GA-09 capacity boundary remains carried forward.
- DB waiting connections remain carried forward.
- Dashboard overview requires `from`, `to`, `limit`, `trendBucket`; `storeId` optional.
- Dashboard build may be skipped by explicit switch.

## Expected status

`PASS CGA-02 PRODUCTION MONITORING INCIDENT WINDOW / GO CGA-03`


## CGA-02.1 known conflict baseline

When CGA-02 is rerun after a documented operator test created known historical sync conflicts before the valid monitoring activity, use `-AllowedExistingSyncConflictCount <count>` to prevent the baseline from masking new blockers. The count must match the known existing conflicts and remains a carried condition, not a public GA approval.
