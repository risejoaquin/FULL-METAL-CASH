# LGA-12 Final Decision Plan

## Final decision
The final decision is evidence-driven and does not activate Public GA.

## Checks
- `/health/live` and `/health/ready` must remain available.
- Concurrency 3 is probed with six requests.
- p95 readiness target remains <= 1200 ms.
- Waiting connections must remain <= 12.
- Commercial activity must satisfy the existing 24-hour minimums.
- Inventory, cash, sync, RLS, audit and stable release checks must remain healthy.

## Decision mapping
If the capacity probe fails, `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` is mandatory and the final decision may only continue Limited GA or block Public GA until capacity upgrade.

If the capacity probe passes, `CAPACITY_GATE_PASSED` must be recorded. A `RECOMMEND_PUBLIC_GA_REVIEW` outcome is only a recommendation for separate review; Public GA remains not activated by LGA-12.

## Current expected path
`CONTINUE_LIMITED_GA` + `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` + `PUBLIC GA NOT ACTIVATED`.

Current infrastructure outcome: **CAPACITY UPGRADE REQUIRED BEFORE PUBLIC GA**.
