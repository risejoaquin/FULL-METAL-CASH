# Public GA GO/NO-GO Checklist

- [ ] Security / tenant isolation evidence exists and current validation passes.
- [ ] Disaster recovery, backup, restore and rollback evidence exists.
- [ ] Observability / on-call readiness evidence exists.
- [ ] Customer/operator/admin acceptance evidence exists.
- [ ] Stable release exists.
- [ ] Capacity gate passes at concurrency 3, 6 requests, p95 <= 1200 ms.
- [ ] `/health/live` and `/health/ready` return 2xx.
- [ ] Sync pending, processing and retry queues are zero.
- [ ] Existing conflict and dead-letter baselines have not increased.
- [ ] Negative stock is zero.
- [ ] No financial duplicates or negative payments.
- [ ] Waiting connections <= 12 and long-running queries = 0.
- [ ] Required tenant tables and RLS are intact.
- [ ] Schema version 4 / `schema_version_4` preserved.
- [ ] Commercial activity minimums are met.
- [ ] Public GA not activated by this review.
- [ ] Rollback remains available before activation.
