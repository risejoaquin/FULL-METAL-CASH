# Post-LGA Capacity Evidence Matrix

| Evidence | Required result |
|---|---|
| Entry gate | LGA-12 PASS reviewed |
| Readiness source | Single `unnest(@required_tables::text[])` catalog query |
| Build/tests | PASS |
| Secret scan | PASS |
| `/health/live` | 2xx |
| `/health/ready` | 2xx |
| Capacity probe | concurrency 3 / 6 requests |
| Live p95 | <= 1200 ms |
| Ready p95 | <= 1200 ms |
| Capacity gate passed | `True` |
| Waiting connections | <= 12 |
| Long-running queries | 0 |
| Negative stock | 0 |
| Sync queues | pending/processing/retry = 0 |
| Schema | schema version 4 / `schema_version_4` |
| Commercial operations | Healthy |
| Public GA | NOT_ACTIVATED |
| Next decision | Public GA readiness review authorized, activation not performed |

- Public GA not activated.
