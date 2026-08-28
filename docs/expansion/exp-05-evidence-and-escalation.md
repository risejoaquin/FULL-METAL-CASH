# EXP-05 Evidence and Escalation

## Evidence mínima

Cada ejecución de EXP-05 debe conservar:

- PowerShell output completo desde [EXP-05].
- operational-monitoring-hardening-manifest.json.
- exp-05-operational-monitoring-hardening-log.md.
- SQL cross-check output.
- observability/metrics response shape validada por el script.

## Escalation

| Tipo | Owner inicial | Escalación |
|---|---|---|
| Health/readiness | Ops owner | Backend owner |
| Database | Backend owner | DBA/cloud owner |
| Sync retry/dead_letter | Sync owner | Backend owner |
| Pending conflict | Sync owner | Product owner |
| Failed payment | Payments owner | Backend owner |
| Cash difference | Store manager | Ops owner |
| Negative inventory | Inventory owner | Product/backend owner |
| Failed requests/latency | Backend owner | Infrastructure owner |

## Decision log

Toda decisión debe registrar:

- timestamp.
- owner.
- metric.
- threshold.
- action.
- result.
- GO/NO-GO decision.
