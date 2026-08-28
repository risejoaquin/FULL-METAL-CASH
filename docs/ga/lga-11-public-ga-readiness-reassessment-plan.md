# LGA-11 Public GA Readiness Reassessment Plan

## Public GA readiness reassessment
Reassess the real production baseline after LGA-10 without widening rollout scope.

### Capacity and health
- `/health/live` must return 2xx.
- `/health/ready` must return 2xx.
- concurrency 3 is probed with the configured request count.
- p95 readiness target remains <= 1200 ms.
- waiting connections remain <= 12.
- long-running queries remain zero.
- Railway remains the known infrastructure constraint until the capacity gate passes.

### Operational continuity
- sales, payments and receipts remain above minimum volume;
- cash shifts remain reconciled;
- negative stock remains zero;
- dashboard and reports remain available;
- audit and support operations remain active;
- sync queues remain clean except for formally accepted conflict/dead-letter baselines;
- schema version 4 remains authoritative.

## Decision rules
If capacity fails: `KEEP_LIMITED_GA` + `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`.

If capacity passes: record `CAPACITY_GATE_PASSED`; any `RECOMMEND_PUBLIC_GA_REVIEW` is recommendation-only and requires separate human review.

Public GA NOT ACTIVATED by LGA-11 under every path.

Capacity decision wording: CAPACITY UPGRADE REQUIRED BEFORE PUBLIC GA.
