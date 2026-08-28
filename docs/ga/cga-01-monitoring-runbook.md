# CGA-01 Monitoring Runbook

Monitor during the controlled rollout:

- `/health/live`
- `/health/ready`
- `/api/v1/observability/metrics`
- `/api/v1/sync/status`
- `/api/v1/sync/contract`
- `/api/v1/reports/dashboard/overview`
- `/api/v1/reports/sales/range`

Watch conditions:

- waiting connections
- long running queries
- pending conflicts
- retry pending events
- stale processing events
- dead letter events
- duplicate local sales
- low stock items

Rollback triggers:

- financial duplication or loss
- tenant isolation/RLS drift
- persistent health/ready failures
- persistent sync conflicts or stale processing
- dashboard/reporting failure during operation
- public GA accidentally activated
