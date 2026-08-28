# CGA-02 — 24h/72h Production Monitoring and Incident Window

## Purpose

CGA-02 validates the controlled rollout through a production monitoring and incident window. It is a monitoring gate after CGA-01 and does not activate Public GA.

## Scope

- Phase: CGA-02
- Mode: LIMITED
- Window: 24h/72h
- Public GA: NOT ACTIVATED
- `publicGeneralAvailabilityActivated=False`
- `generalAvailabilityActivated=False`

## Entry gate

CGA-01 must be PASS: `PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02`.

## What is monitored

- `health/live`
- `health/ready`
- protected `observability/metrics`
- `sync/status`
- `sync/contract`
- `reports/sales/range`
- `reports/dashboard/overview`
- dashboard URL availability
- DB pressure
- waiting connections
- sync conflicts
- dead-letter/stale processing events
- financial duplicate checks
- RLS drift

## Dashboard overview contract

The dashboard overview endpoint requires:

- `from`
- `to`
- `limit=20`
- `trendBucket=day`
- optional `storeId`

## Known carried-forward conditions

- GA-09 capacity boundary: Concurrency 3+ can return upstream 400 in the current Railway/upstream path.
- DB waiting connections observed across GA-10/GA-11/GA-12/Post-GA-12/CGA-01.
- Dashboard build may be `SKIPPED_BY_SWITCH` if explicitly skipped.
- Public GA requires a separate explicit decision.

## Decision

PASS means the limited controlled rollout monitoring window has no blocking incident in the sampled evidence and may proceed to CGA-03.


## CGA-02.1 known conflict baseline

When CGA-02 is rerun after a documented operator test created known historical sync conflicts before the valid monitoring activity, use `-AllowedExistingSyncConflictCount <count>` to prevent the baseline from masking new blockers. The count must match the known existing conflicts and remains a carried condition, not a public GA approval.
