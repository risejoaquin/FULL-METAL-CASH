# EXP-05 Monitoring Owner Threshold Matrix

| Metric | Owner | GO threshold | Condition threshold | NO-GO threshold | Action | Evidence |
|---|---|---:|---:|---:|---|---|
| health/live | Ops owner | alive | n/a | not alive | Escalate incident | PowerShell log |
| health/ready | Ops owner | ready | n/a | not ready | Stop expansion | readiness response |
| database.ready | Backend owner | true/ready | n/a | false/not ready | DB triage | metrics/log |
| required tables | Backend owner | true | n/a | false | migration/schema triage | SQL cross-check |
| sync retry_pending | Sync owner | 0 | >0 monitored | growth or unowned | monitor_retry_pending_sync | metrics + SQL |
| sync dead_letter | Sync owner | 0 | >0 triaged | untriaged growth | triage_known_dead_letter | dead-letter evidence |
| pending conflicts | Sync owner | 0 | n/a | >0 | stop expansion, resolve conflict | conflicts endpoint |
| failed payments | Payments owner | 0 last 24h | n/a | >0 last 24h | payment triage | metrics + SQL |
| failed requests | Backend owner | 0 | n/a | >0 | inspect logs | observability metrics |
| p95 latency | Backend owner | <= 5000 ms | monitored | > 5000 ms | latency triage | metrics |
| negative inventory | Inventory owner | 0 | >0 reconciled | unexplained growth | inventory_reconciliation_required | SQL derived stock |
| cash shift difference | Cash owner | 0 last 24h | n/a | >0 last 24h | cash review | SQL cross-check |
| open cash shift | Store manager | expected live shifts | review | stale/unowned | daily cash review | SQL cross-check |
| audit events | Support owner | present | low volume review | missing | audit triage | audit endpoint |

## Regla

Toda métrica con condición debe tener owner, threshold, action y evidence antes de avanzar.
