# CGA-01 — Controlled GA Rollout Execution

Status target: `PASS CGA-01 CONTROLLED GA ROLLOUT EXECUTION / GO CGA-02`.

CGA-01 executes the approved controlled rollout after Post-GA-12. It is `LIMITED`, does not activate public GA, and keeps `publicGeneralAvailabilityActivated=False` and `generalAvailabilityActivated=False`.

## Scope

- RolloutMode: `LIMITED`
- MaxStores: 2
- MaxConcurrentTerminals: 2
- ObservationWindowHours: 24 to 72
- Public GA: NOT ACTIVATED

## Dashboard overview API contract

Dashboard overview must be called with the full contract:

`/api/v1/reports/dashboard/overview?from=<from>&to=<to>&limit=20&trendBucket=day`

`storeId` is optional for store-scoped validation.

## Carried conditions

- GA-09 capacity boundary: Concurrency 3+ current Railway/upstream path can return 400 upstream error.
- GA-10/GA-11/GA-12/Post-GA-12 database waiting connections observation is carried forward.
- Public GA activation requires explicit separate change.

## Pass criteria

- health/live and health/ready return 200.
- Observability without auth returns 401 and with auth reports database ready.
- Tenant/store/users/roles/permissions/catalog are readable.
- Sales range and dashboard overview pass with the correct contract.
- Sync contract remains schemaVersion 4 / syncContract schema_version_4.
- No duplicate local sales, no pending conflicts, no stale processing events, no RLS drift.
- Public GA remains disabled.
