# GA-11 — Evidence Matrix

| Area | Evidence | Blocking rule |
|---|---|---|
| Customer acceptance | customers endpoint, customer counts, sales/receipt readiness evidence | API failure or undisclosed known capacity condition blocks. |
| Operator acceptance | catalog, sales, dashboard, inventory, sync status | 4xx/5xx on required operator endpoint blocks. |
| Admin acceptance | tenant, stores, users, roles, permissions, observability | missing admin endpoint or auth drift blocks. |
| Sync contract | `/api/v1/sync/contract` and DB snapshot | schema drift blocks. |
| RLS | DB RLS tenant table check | any RLS missing table blocks. |
| Integrity | duplicate sale, conflicts, legacy schema events | any non-zero blocking count blocks. |
| Capacity conditions | GA-09 Concurrency 3+ upstream error, GA-10 db_waiting_connections_11 | carried to GA-12 unless resolved or accepted. |
| Launch state | `generalAvailabilityActivated=False` | public GA activation blocks. |
