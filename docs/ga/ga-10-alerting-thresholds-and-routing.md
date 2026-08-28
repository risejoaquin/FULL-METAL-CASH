# GA-10 — Alerting Thresholds and Routing

## Alertas obligatorias

| Señal | Umbral inicial | Severidad | Dueño | Acción |
|---|---:|---|---|---|
| health/live no 200 | cualquier ocurrencia sostenida | SEV1 | Platform On-call | revisar API/proxy/deploy |
| health/ready no 200 | cualquier ocurrencia sostenida | SEV1 | Platform On-call + DB Owner | revisar DB/readiness |
| upstream error 400 | >= 2% en ventana operativa | SEV2 | Platform On-call | revisar Railway/proxy/capacity |
| 5xx API | >= 1% | SEV2/SEV1 | Backend Owner | triage backend/logs |
| timeout | >= 1% | SEV2 | Platform On-call | revisar hosting, DB, pool |
| p95 latency | > 2500 ms para GA-09 profile | SEV2 | Backend/Platform | performance triage |
| p99 latency | > 5000 ms para GA-09 profile | SEV2 | Backend/Platform | capacity triage |
| failed request rate | >= 1% / 15m | SEV2 | Platform On-call | incident intake |
| sync retry over SLA | > 0 | SEV2 | Sync Owner | sync runbook |
| new dead-letter | > 0 | SEV2 | Sync Owner + Support | triage/retry/quarantine |
| failed/declined payment | > 0 unexplained | SEV1 | Payments Owner | payment integrity incident |
| RLS drift | any missing RLS/policy | SEV1 | Security/Backend | block launch |

## GA-09 capacity boundary routing

If `Concurrency 3+` again produces `400 upstream error`, classify as hosting/proxy/capacity until Railway logs or application logs prove a backend defect. Actions:

1. capture exact time window;
2. inspect Railway service logs;
3. inspect CPU/RAM/restarts;
4. inspect DB connections;
5. compare health/live vs health/ready;
6. decide: scale host, change deployment topology, or formally accept lower launch capacity.

This condition blocks public GA activation if unresolved or unaccepted by GA-12.
