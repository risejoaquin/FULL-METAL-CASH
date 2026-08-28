# LGA-09 — Limited GA Stability Confirmation / Capacity Risk Review

## Objective

Confirm accumulated Limited GA stability after the real-production PASS of LGA-08 and determine whether the known infrastructure capacity risk remains acceptable for continued Limited GA operation.

LGA-08 is the mandatory entry gate. LGA-09 does not activate Public GA and does not widen the production scope.

## Mandatory invariants

- Public GA NOT ACTIVATED.
- General Availability activation flags remain false.
- Sync schema version 4 and `schema_version_4` remain authoritative.
- Negative stock baseline remains exactly zero.
- Allowed waiting connections remains 12; the baseline must not be raised by this phase.
- Existing sync conflicts remain at or below the formally accepted baseline of 3.
- Dead letters remain at or below the formally accepted baseline of 1.
- Limited GA scope remains max 2 stores and max 2 concurrent terminals operationally authorized.
- No automatic promotion to LGA-10 or Public GA.

## Stability confirmation

LGA-09 reviews:

- health live and health ready;
- database pressure and long-running queries;
- waiting connections;
- readiness/live p95 under concurrency 3;
- active stores and available terminals;
- sync queues, conflicts and dead letters;
- audit activity;
- recent completed sales, payments and receipts;
- inventory and negative-stock regression;
- dashboard availability and reporting read models;
- RLS and schema integrity.

## Capacity risk decision

Allowed decisions:

- `CONTINUE_LIMITED_GA`
- `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`

If the concurrency-3 capacity probe fails, LGA-09 requires `CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA` and formal limited-capacity acceptance. This is a PASS for Limited GA stability only; it is not Public GA readiness.

## Exit

A PASS means the current Limited GA operation is stable within its accepted boundaries. Public GA remains NOT ACTIVATED. Advancement to LGA-10 is not authorized until the LGA-09 PASS logs are reviewed.
