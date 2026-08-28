# SolidPOS — GA-12 Final General Availability Launch Readiness

GA-12 consolidates GA-01 through GA-11 and produces a final readiness decision.

Expected result:

```text
PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT
```

General Availability public activation remains **NOT ACTIVATED** by this package. GA-12 does not activate public GA; activation requires a separate explicit decision.

Known conditions carried forward:

- GA-09 capacity boundary: Concurrency 3+ can return 400 upstream error in the current Railway/upstream path.
- GA-10/GA-11 DB observation: waitingConnectionCount = 11, requiring monitoring/tuning before public GA activation.
- Dashboard build may be skipped only when deployed dashboard URL validates.


## Hotfix 12.3

GA-12.3 fixes the final DB readiness SQL snapshot to support `pos.sync_inbox_events` schemas that use `created_at` instead of `updated_at` for stale processing detection.
