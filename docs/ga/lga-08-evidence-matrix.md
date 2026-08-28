# LGA-08 — Evidence Matrix

| Gate | Evidence | PASS rule | Blocking condition |
|---|---|---|---|
| Entry | LGA-07 runtime manifest or prior PASS logs | LGA-07 PASS | Missing LGA-07 closure |
| Build | `dotnet restore/build/test` | all exit 0 | any failure |
| Secrets | local secret scan | exit 0 | detected local secret |
| WPF | QSR command refresh + visual confirmation | confirmed | missing confirmation |
| API health | `/health/live`, `/health/ready` | 2xx | non-2xx |
| Observability | authenticated metrics | DB ready=true | DB not ready |
| Sync contract | API + DB snapshot | schema version 4 | legacy/different schema |
| Sync queue | status + DB | pending/processing/retry=0 | nonzero transient queue |
| Sync conflict | DB snapshot | <=3 | baseline increase |
| Dead letter | DB snapshot | <=1 | baseline increase |
| Inventory | ledger snapshot | negative stock=0 | any negative stock |
| DB capacity | `pg_stat_activity` | waiting connections <=12 | baseline exceeded |
| Financial | sales/payments | no duplicates/negative payments | integrity regression |
| Activity | last 24h | sales>=6, payments>=6, receipts>=3 | insufficient decision window |
| RLS | PostgreSQL catalog | no tenant table missing RLS | isolation regression |
| Capacity | live/ready concurrency probe | mandatory only in post-upgrade verification | post-upgrade probe fails |
| Decision | validator manifest | KEEP_LIMITED_GA | any Public GA activation |

## Decision semantics

The evidence matrix supports only LGA-08 post-upgrade verification or continued monitoring. A successful capacity decision does not activate Public GA. The validator always persists `publicGaActivation = NOT_ACTIVATED` and does not authorize a subsequent phase automatically.
