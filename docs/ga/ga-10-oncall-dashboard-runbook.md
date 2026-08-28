# GA-10 — On-call Dashboard Runbook

## Daily checks

- Confirm `/health/live` is 200.
- Confirm `/health/ready` is 200.
- Login as admin without exposing password/token.
- Confirm `/api/v1/observability/metrics` returns database, request, sync, sales, payments, inventory and audit sections.
- Confirm dashboard overview endpoint returns 200.
- Confirm sync status and sync contract return 200 and schema version 4.
- Confirm no new retry-over-SLA, legacy schema events, duplicate sales/payments, or RLS drift.

## Incident workflow

1. Classify severity.
2. Capture timestamp, endpoint, status, latency, tenant, and deployment revision.
3. Do not paste secrets or tokens into incident notes.
4. Check Railway logs for upstream errors, restarts, memory, CPU and proxy errors.
5. Check DB pressure and connection count.
6. If data integrity is affected, freeze destructive remediation and use append-only evidence.
7. Escalate to owner.
8. Document recovery and post-incident review.

## GA-09 condition response

If upstream errors appear at `Concurrency 3+`, do not mark as backend bug without logs. Treat as capacity condition first and decide between host scaling, Railway plan/region changes, topology split, or accepted launch limitation.
