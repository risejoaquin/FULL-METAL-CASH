# LGA-07 Capacity Monitoring Checklist

## Operational Scope

- limited ga remains active.
- public ga not activated.
- capacity monitoring is required.
- support window remains active.

## Checks

- health live status is 200.
- health ready status is 200.
- observability remains protected with 401 when unauthenticated.
- inventory endpoint remains available.
- sales/dashboard completed sales read models remain consistent.
- sync pending, processing, and retry queues remain clean.
- conflict and dead-letter baselines do not increase.
- negative stock remains zero.
- open shift count remains within allowed baseline.
- waiting connections remain within allowed baseline.
- long-running queries remain zero.

## Decision

If capacity passes configured thresholds, move to post-upgrade verification. If capacity does not pass but blocker matrix is empty, continue Limited GA monitoring.
