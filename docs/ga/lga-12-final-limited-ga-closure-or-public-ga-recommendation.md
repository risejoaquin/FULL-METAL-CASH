# LGA-12 — Final Limited GA Closure or Public GA Recommendation

## Objective
LGA-12 is the final Limited GA decision gate after reviewed LGA-11 PASS evidence. It consolidates commercial confidence, operational continuity, data integrity, sync integrity, database pressure, capacity, and Public GA readiness without activating Public GA.

## Entry gate
- LGA-11 reviewed PASS.
- Public GA remains not activated.
- schema version 4 and `schema_version_4` remain authoritative.

## Final decision options
1. `RECOMMEND_PUBLIC_GA_REVIEW` — allowed only when the capacity gate passes and all other blockers remain zero. This is a recommendation for human review, not activation.
2. `CONTINUE_LIMITED_GA` — continue Limited GA under the accepted capacity risk.
3. `BLOCK_PUBLIC_GA_UNTIL_CAPACITY_UPGRADE` — formally block Public GA until capacity is remediated.

## Current expected decision
With the known Railway capacity limitation, the expected result is `CONTINUE_LIMITED_GA`, `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`, and `PUBLIC GA NOT ACTIVATED`.

## Non-negotiable invariants
- Public GA not activated.
- schema version 4.
- Negative stock count = 0.
- Waiting connections must not exceed 12.
- Conflict baseline <= 3 and dead-letter baseline <= 1.
- No open shifts or cash differences above accepted thresholds.
- No duplicate local sales, negative payments, legacy schema events, stale processing, missing RLS, or long-running queries.

## Scope
The gate rechecks health, capacity, database pressure, stores and terminals, commercial operations, cash shifts, inventory, reports/dashboard, audit activity, support evidence, sync queues and release state before recording the final Limited GA decision.

The enforced public decision is **KEEP LIMITED GA**; this package never activates Public GA.
