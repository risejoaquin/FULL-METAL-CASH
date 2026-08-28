# GA-02 — Sync Queue and SLA Closure

Status: **PENDING USER VALIDATION**

## Objective
Close inherited synchronization queue and SLA conditions before GA support/SLO readiness. GA remains disabled.

## Entry gate
Fresh GA-01 must return `PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02`, with `blockers = {}`, `schemaVersion = 4`, `syncContract = schema_version_4`, and `generalAvailabilityActivated = False`.

## Source-of-truth checks
- retry_pending and retry due
- retry over 15-minute SLA
- stale processing over 15 minutes
- pending conflicts
- processed schema-4 events
- legacy schema events
- new and untriaged dead-letter events
- duplicate `(tenant, terminal, batch, sequence)` identity
- duplicate `(tenant, terminal, event_id)` identity
- dead-letter classification and audit evidence

## Safe remediation policy
No commercial or ambiguous sync event is mutated automatically. Only an over-SLA `retry_pending` row that is independently identifiable as a controlled SolidPOS validation fixture may be closed as historical evidence. The row is retained and moved to `rejected`; no DELETE is used. An append-only `audit_events` row records before/after state and reason.

A triaged controlled dead-letter that predates the fresh GA-01 baseline is never deleted or rewritten. It may remain only with an append-only audit decision `close_as_historical_evidence`, proving it is not executable pending work.

## Exit gate
```text
retryPendingCount = 0
retryOverSlaCount = 0
staleProcessingCount = 0
pendingConflictCount = 0
newDeadLetterCount = 0
untriagedDeadLetterCount = 0
legacySchemaEventCount = 0
```

The historical dead-letter may remain only when classified as controlled validation evidence with no executable work pending. Any commercial, ambiguous, new, or untriaged dead-letter is a blocker.

Required result:
```text
PASS GA SYNC QUEUE SLA CLOSURE / GO GA-03
```
