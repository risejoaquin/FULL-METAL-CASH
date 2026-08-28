# LGA-11 Evidence Matrix

| Area | Evidence | PASS rule |
|---|---|---|
| Entry gate | LGA-10 manifest/log | LGA-10 PASS reviewed |
| Health | live/ready HTTP status | both 2xx |
| Capacity | concurrency-3 probes | result recorded and decision consistent |
| p95 | live/ready probe latency | compare to <= 1200 ms threshold |
| DB pressure | pg_stat_activity snapshot | waiting <= 12; long-running = 0 |
| Stores/terminals | DB rollout scope | >=1 active store, within Limited GA cap; terminal available |
| Commercial operations | API + DB | sales/payments/receipts above minimum |
| Cash operations | closed/open shifts + differences | closed history exists; open/difference within baseline |
| Inventory | API + DB ledger | negative stock = 0 |
| Dashboard/reports | protected APIs + deployed URL | 2xx / coherent activity |
| Audit/support | DB audit count + runbooks | activity present; docs present |
| Sync | API + DB | queues clean; accepted conflict/dead-letter baselines unchanged |
| RLS/security | DB + secret scan | complete / no obvious secrets |
| Schema | sync contract + DB | schema version 4 / schema_version_4 |
| Public GA state | DB + validator manifest | NOT_ACTIVATED / false |
| Readiness decision | manifest | KEEP_LIMITED_GA while capacity fails |
| Capacity recommendation | manifest | CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA while probe fails |

This evidence matrix supports reassessment only. It cannot activate Public GA.
