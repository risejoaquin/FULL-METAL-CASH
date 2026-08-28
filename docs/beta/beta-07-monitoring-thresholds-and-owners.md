# BETA-07 Monitoring Thresholds and Owners

| Signal | Threshold | Classification | Owner | Action |
|---|---:|---|---|---|
| health/readiness | not ready | blocker | platform | incident + containment |
| pending conflict | > 0 | blocker | sync/support | incident + conflict triage |
| stale processing | > 0 | blocker | sync/platform | incident |
| failed payment 24h | > 0 | blocker | payments/support | investigate before GO |
| cash difference 24h | > 0 | blocker | store/support | reconcile before GO |
| audit events 24h | 0 | blocker | platform/security | restore audit evidence |
| retry pending/due | > 0 | condition | sync/support | worker/manual retry decision |
| dead-letter | > 0 | condition | support | triage/quarantine/correct |
| open shift | > 0 | condition | store operations | daily review |
| negative inventory | > 0 | condition | inventory | reconciliation |
| low stock | > 0 | condition | store/inventory | replenishment review |

Support owns documented escalation when any condition exceeds its operational threshold or becomes a blocker.
