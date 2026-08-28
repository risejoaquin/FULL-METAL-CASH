# CGA-02 Evidence Matrix

| Area | Evidence | Required result |
|---|---|---|
| Entry gate | CGA-01 PASS manifest or revalidation | PASS |
| Build | dotnet restore/build/test | PASS |
| Secrets | local secret scan | PASS |
| Health | health/live and health/ready | 200 |
| Observability | unauthenticated metrics | 401 |
| Observability | authenticated metrics database.ready | True |
| Sync | sync/status | pending/retry/conflict 0 |
| Sync contract | sync/contract | schemaVersion 4, syncContract schema_version_4 |
| Reports | sales range | 200 |
| Dashboard | dashboard overview | 200 with dashboard overview contract |
| Dashboard | external dashboard URL | 2xx/3xx |
| Financial | duplicate local sales | 0 |
| Sync integrity | legacy schema events | 0 |
| Sync integrity | stale processing | 0 |
| RLS | rls missing tables | 0 |
| DB | long-running queries | 0 |
| Launch safety | Public GA | NOT ACTIVATED |

Required terms: dashboard overview, sales range, schemaVersion 4, syncContract schema_version_4.


## CGA-02.1 known conflict baseline

When CGA-02 is rerun after a documented operator test created known historical sync conflicts before the valid monitoring activity, use `-AllowedExistingSyncConflictCount <count>` to prevent the baseline from masking new blockers. The count must match the known existing conflicts and remains a carried condition, not a public GA approval.
