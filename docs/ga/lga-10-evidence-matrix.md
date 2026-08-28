# LGA-10 Evidence Matrix

| Area | Evidence | PASS rule |
|---|---|---|
| Entry gate | LGA-09 manifest/log | LGA-09 PASS reviewed |
| Sales | API + DB 24h counts | >= configured minimum |
| Payments | DB 24h count | >= configured minimum, no negative payments |
| Receipts | DB 24h count | >= configured minimum |
| Cash shifts | DB closed/open shifts | closed history exists; open count within baseline |
| Cash reconciliation | DB difference count | zero differences in last 24h |
| Inventory | API + DB ledger | negative stock = 0 |
| Dashboard | dashboard overview + URL | 2xx and coherent sales activity |
| Reports | sales range + dashboard overview | protected endpoints return data |
| Audit | DB 24h count | >= configured minimum |
| Support operations | GA support/on-call docs + observability | present and DB ready |
| Sync | API + DB | queues clean; accepted conflict/dead-letter baselines unchanged |
| Security | secret scan + RLS | no obvious secrets; RLS complete |
| Capacity | concurrency-3 probe | recorded; formal Limited GA capacity acceptance preserved if failing |
| Schema | sync contract + DB | schema version 4 / schema_version_4 |
| Public GA | validator + DB flags | NOT_ACTIVATED / false |
| Decision | manifest | CONTINUE_LIMITED_GA |

This evidence matrix is commercial operations evidence only and cannot authorize Public GA.
