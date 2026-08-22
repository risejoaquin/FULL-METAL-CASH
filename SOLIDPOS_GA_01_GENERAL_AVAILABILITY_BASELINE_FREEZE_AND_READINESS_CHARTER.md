# SolidPOS GA-01 — General Availability Baseline Freeze and Readiness Charter

GA-01 freezes the exact production baseline immediately after a fresh BETA-10 revalidation and establishes the formal entry contract for General Availability Readiness. It does **not** activate General Availability.

Source BETA-10 package SHA-256: `baa99c7ebdf6c5a53dc1de629f3c3bd87a94b42a4be4d137972f97fb54fde484`.

## Entry contract

Required before GA-01 may PASS:

```text
BETA-10 = PASS LIMITED COMMERCIAL BETA CLOSURE / GO GENERAL AVAILABILITY PREP
blockers = {}
schemaVersion = 4
syncContract = schema_version_4
generalAvailabilityActivated = False
```

## Baseline captured

The validator records a repository SHA-256 plus production snapshots for tenant, stores, terminals, users, customers, catalog/pricing/modifiers, sales, payments, receipts, returns/refunds, cash, inventory ledger, sync, audit and release/update state.

Inherited BETA-10 conditions remain visible. GA-01 does not silently close retry/SLA, historical dead-letter, or stable-channel work; those remain explicit handoff items for later GA gates, beginning with GA-02.

## Result

```text
PASS GENERAL AVAILABILITY BASELINE FREEZE / GO GA-02
```

Until the user supplies real production logs, repository delivery status is `PENDING USER VALIDATION`.

## Changes delivered

- Added the GA stage folder and GA-01 validator.
- Added production SQL baseline snapshot covering all GA-01 domains.
- Added deterministic repository baseline hashing and source BETA-10 ZIP provenance.
- Added GA readiness charter, evidence matrix, Go/No-Go contract and runtime evidence contract.
- Added the authoritative General Availability Readiness roadmap to the repository.

## Modules affected

- `scripts/ga` — new validation and SQL source-of-truth.
- `docs/ga` — GA governance, evidence and Go/No-Go documentation.
- repository root — GA roadmap, GA-01 phase report and validation commands.
- No application runtime code, database migrations, API contracts or schema were changed.

## Architectural decisions

- GA-01 is read-only with respect to business state except for safe inherited BETA validators that already own their documented validation-fixture reconciliation behavior.
- The production snapshot uses `inventory_ledger` aggregation instead of a derived inventory table.
- Existing schema/sync/modifier contracts are frozen rather than redesigned.
- Retry/SLA and historical dead-letter conditions are intentionally carried to GA-02 instead of being hidden or artificially cleared in GA-01.
- Stable-channel promotion remains pending for GA-05/GA-06.

## Risks

- Fresh BETA-10 revalidation may surface new production drift; this is intentional and must block GA-01 when material.
- Concurrent commercial activity after the fresh BETA-10 timestamp is recorded as an explicit condition rather than silently ignored.
- `-SkipDashboardBuild` skips compilation only; inherited validators still enforce the dashboard source contract.
- This delivery cannot claim production PASS until the user executes the validator and returns real logs.
