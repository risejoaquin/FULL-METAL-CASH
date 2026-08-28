# LGA-09 Evidence Matrix

| Area | Evidence | PASS rule |
|---|---|---|
| Entry gate | LGA-08 manifest/logs | LGA-08 PASS |
| Health | live + ready statuses | both 2xx |
| Capacity | concurrency 3 probes | capture failures and p95; failed probe requires capacity-upgrade decision |
| DB pressure | pg_stat_activity | waiting <= 12; long-running = 0 |
| Stores | PostgreSQL rollout scope | active 1..2 |
| Terminals | PostgreSQL rollout scope | available >= 1 |
| Sync queues | API + DB | pending/processing/retry/stale = 0 |
| Sync accepted debt | conflicts/dead letters | conflicts <= 3; dead letters <= 1 |
| Sync contract | API + DB | schema version 4 / schema_version_4 |
| Audit | DB monitoring activity | >= configured minimum in last 24h |
| Sales | reports + DB | >= configured minimum in last 24h |
| Payments | DB | >= configured minimum in last 24h |
| Receipts | DB | >= configured minimum in last 24h |
| Inventory | inventory API + ledger | negative stock = 0 |
| Financial integrity | DB | duplicate local sales = 0; negative payments = 0 |
| RLS | catalog inspection | missing tenant RLS tables = 0 |
| Dashboard | URL + reporting endpoints | 2xx / valid read models |
| Decision | validator manifest | CONTINUE_LIMITED_GA or CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA |
| Public GA | manifest + DB flags | NOT_ACTIVATED / false |

The evidence matrix does not authorize Public GA. A capacity probe failure may still PASS LGA-09 only when `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` is recorded and Limited GA capacity risk remains formally accepted.
