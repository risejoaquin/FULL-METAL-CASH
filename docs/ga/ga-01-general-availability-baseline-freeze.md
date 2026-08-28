# GA-01 General Availability Baseline Freeze

## Purpose

Create the first auditable General Availability Readiness baseline from the last validated BETA-10 repository and a fresh production revalidation.

## Guardrails

- Re-run BETA-10; do not trust an old runtime manifest alone.
- Preserve `schemaVersion = 4` and `syncContract = schema_version_4`.
- Preserve `inventory_ledger` as inventory source of truth.
- Preserve modifier semantics `none | add | substitute` and `substitute + replacesProductId`.
- General Availability must remain not activated.
- No destructive mutation is performed by GA-01.
- Inherited conditions remain visible until corrected, formally accepted, or proven historical/non-actionable.

## Snapshot domains

Tenant, stores, terminals, users, customers, catalog/pricing/modifiers, sales, payments, receipts, returns/refunds, cash, inventory, sync, audit and release/update.

## Blocking drift

Schema-contract drift, new/untriaged dead-letter, stale sync processing, pending conflict, negative inventory, open cash shift, recent unexplained cash difference, missing/invalid beta release, catalog modifier semantic drift or premature GA activation prevent PASS.

## Exit

`PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02`.
