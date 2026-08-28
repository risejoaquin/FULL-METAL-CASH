# LGA-12 Closure Checklist

- [ ] LGA-11 reviewed PASS.
- [ ] `/health/live` returns 2xx.
- [ ] `/health/ready` returns 2xx.
- [ ] Concurrency 3 probe captured.
- [ ] p95 readiness compared against 1200 ms.
- [ ] Waiting connections <= 12.
- [ ] DB pressure has no long-running query blocker.
- [ ] Commercial operations meet sales, payment and receipt minimums.
- [ ] Cash shifts are closed and no cash differences exceed threshold.
- [ ] Negative stock = 0.
- [ ] Sync queues pending/processing/retry are zero.
- [ ] Conflict/dead-letter baselines not increased.
- [ ] Audit activity present.
- [ ] Dashboard/reporting endpoints healthy.
- [ ] Support and on-call documentation present.
- [ ] schema version 4 / `schema_version_4` retained.
- [ ] Public GA not activated.
- [ ] Final decision and capacity recommendation recorded.
