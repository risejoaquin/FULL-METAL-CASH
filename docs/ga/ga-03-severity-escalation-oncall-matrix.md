# GA-03 — Severity, Escalation and On-call Matrix

| Severity | Typical impact | Acknowledge | Escalation | Update cadence | Primary owner |
|---|---|---:|---:|---:|---|
| SEV1 | service/readiness unavailable; database unavailable; tenant isolation/security; payment or cash integrity uncertain; data corruption; rollback/restore failure | <=15 min | immediate | <=30 min | Incident Commander / Platform On-call |
| SEV2 | degraded checkout; sync backlog/stale processing/dead-letter; inventory inconsistency; release/update degradation; sustained SLO breach | <=30 min | <=60 min if unresolved | <=60 min | Support Lead + Domain Owner |
| SEV3 | localized non-blocking defect, receipt/email issue, operational warning with safe workaround | <=4 h | same business day if unresolved | daily | Support Lead |
| SEV4 | documentation, low-impact request, cosmetic/non-operational issue | <=1 business day | planned queue | as agreed | Support Owner |

## On-call ownership

Roles, not personal contact data, are committed to the repository:

- **Incident Commander / Platform On-call:** owns SEV1 command, containment and cross-domain escalation.
- **Support Lead:** owns intake, incident ID, severity, evidence completeness, customer/status communication and handoff.
- **Domain Owner:** Sync, Payments, Cash/Operations, Inventory, Security or Backend owner depending on signal.
- **Release Owner:** owns release freeze, rollback execution and stable-channel incident routing.
- **Operations Owner:** owns store/cash/inventory reconciliation and operator coordination.

## Escalation policy

1. SEV1: page Incident Commander and affected Domain Owner immediately; freeze promotion/deploy when relevant.
2. SEV2: Support Lead owns triage; escalate to Domain Owner; promote to Incident Commander if unresolved inside 60 minutes or impact grows.
3. SEV3/SEV4: ticketed support queue with owner and next action.
4. Missing owner, missing escalation route, or rollback authority is a GA-03 blocker.
