# GA-03 — Domain Incident Routing Runbook

| Domain / signal | Severity baseline | First containment | Owner | Escalation / recovery |
|---|---|---|---|---|
| sync retry/stale processing | SEV2 | prevent duplicate manual writes; inspect event/idempotency/error | Sync Owner | Support Lead -> Incident Commander if >60m |
| new dead-letter | SEV2 | quarantine logically; preserve payload metadata/evidence; no destructive delete | Support Lead + Sync Owner | explicit retry/quarantine/supersede decision |
| pending sync conflict | SEV2 | preserve both versions; no blind LWW | Sync Owner | documented conflict decision by evidence |
| payment failure/integrity | SEV1 | stop unsafe payment flow; preserve tender/payment evidence | Payments Owner | Incident Commander; reconcile before reopen |
| cash difference / drawer integrity | SEV1 | freeze unsafe close; preserve shift/movement evidence | Operations Owner | Incident Commander; reconcile before close |
| negative inventory / reconciliation failure | SEV2, SEV1 if broad integrity impact | stop blind stock edits; inspect inventory_ledger | Inventory + Operations Owner | append-only reconciliation and root cause |
| API/readiness/database | SEV1 | freeze promotion/deploy; terminals use authorized offline path when safe | Platform On-call | DB/platform recovery; rollback/restore decision |
| release/update incident | SEV2, SEV1 if widespread outage/integrity | stop cohort promotion | Release Owner | rollback to last verified release |
| auth/tenant isolation/security | SEV1 | contain access; freeze affected admin/release operations | Security/Platform Owner | credential/config isolation, audit and incident command |

## Retry / dead-letter triage

GA-02 already closed executable retry backlog. The retained historical PILOT-06 dead-letter remains immutable evidence with `close_as_historical_evidence`; it is not executable work. Any dead-letter created after the fresh GA-02 timestamp is a new incident.

## Rollback authority

- **SEV1:** Incident Commander can order containment; Release Owner is authorized to execute application/release rollback. Database restore requires the backup/restore authority/runbook and is never improvised.
- **SEV2:** Release Owner may roll back when the affected release is the likely cause and evidence supports it.
- No destructive deletion of audit, sync, cash, payment or inventory history is a valid rollback strategy.
