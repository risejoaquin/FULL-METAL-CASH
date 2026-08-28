# EXP-08 — SEV Classification Matrix

| Severity | Trigger | Owner | Response | Escalation |
|---|---|---|---|---|
| SEV1 | API down, database not ready, payment outage, data corruption | Engineering owner | Immediate containment | escalate to engineering and ops |
| SEV2 | dead-letter growth, retry due over SLA, cash difference, sync degradation | Support owner | Same-day triage | escalate if unresolved |
| SEV3 | single failed request, open cash shift review, warning-only condition | Support owner | Daily review | handoff if repeated |

Every incident must have owner, response, evidence, escalation decision, and next action.
