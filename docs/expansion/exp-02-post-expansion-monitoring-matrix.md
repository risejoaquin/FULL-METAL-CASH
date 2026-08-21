# EXP-02 Post-Expansion Monitoring Matrix

| Metric | Owner | Threshold | Action |
| --- | --- | --- | --- |
| readiness | Support | not ready | NO-GO |
| retry_pending | Support | > baseline | monitor_retry_pending_sync |
| dead_letter | Engineering | > known baseline | triage_known_dead_letter |
| pending conflicts | Engineering | > 0 | NO-GO |
| failed payments | Support | > 0 last 24h | NO-GO |
| negative inventory | Operations | > 0 | inventory reconciliation |
| audit trail | Support | missing events | NO-GO |

Each metric has owner, threshold and action.
