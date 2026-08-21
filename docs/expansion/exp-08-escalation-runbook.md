# EXP-08 — Escalation Runbook

## Escalation triggers

- SEV1 event
- retry over SLA that cannot be manually resolved
- dead-letter without clear reason
- repeated failed payments
- pending conflict affecting sale/cash/inventory
- suspected data inconsistency

## Handoff

Each escalation must include:

- owner
- sev
- summary
- exact endpoint or SQL evidence
- affected tenant/store/terminal
- last known good state
- containment applied
- rollback considered
- next action

Support owns triage. Engineering owns code, schema, sync processor, and rollback decisions.
