# GA-12 Evidence Matrix

| Area | Required evidence | Expected |
|---|---|---|
| GA-01..GA-11 | Prior real production PASS logs or runtime manifests | PASS |
| Health | `/health/live`, `/health/ready` | 200 |
| Auth | protected endpoint without auth | 401 |
| Dashboard | deployed DashboardUrl | 2xx/3xx |
| Observability | `/api/v1/observability/metrics` protected and DB ready | PASS |
| Acceptance | customer/operator/admin acceptance from GA-11 | PASS |
| Sync | `schemaVersion=4`, `syncContract=schema_version_4` | PASS |
| Integrity | duplicate sales, pending conflicts, legacy schema events | 0 |
| RLS | tenant tables missing RLS | 0 |
| Launch safety | `generalAvailabilityActivated=False` | PASS |
| Known conditions | GA-09 capacity boundary and DB waiting connection observation | carried forward |
