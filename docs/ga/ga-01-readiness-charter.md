# GA-01 Readiness Charter

## Scope freeze

General Availability Readiness is a closure/hardening stage for the existing SolidPOS product. GA-01 establishes the source baseline against which subsequent GA gates operate.

## Non-goals

GA-01 does not add unrelated product features, fiscal/SAT functionality, ERP expansion, arbitrary UI redesigns, stack migrations, schema-version changes, stable rollout or General Availability activation.

## Closed architectural contracts

- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `inventory_ledger` is inventory source of truth
- modifier behavior is `none | add | substitute`
- substitution requires `substitute + replacesProductId`
- offline/sync uses Outbox / Inbox and idempotency
- tenant isolation uses tenant context + PostgreSQL RLS
- auth uses JWT + refresh rotation
- update packaging uses Velopack

## Inherited conditions

The charter explicitly carries forward, when present:

- `retry_pending_sync_requires_ga_readiness_closure`
- `retry_over_sla_requires_ga_readiness_closure`
- `known_dead_letter_triaged_and_stable`
- `stable_channel_promotion_pending`

GA-01 records these conditions but does not convert them into fake blockers or silently remove them. GA-02 owns sync queue/SLA closure.

## Governance

Every GA phase remains gated by real user-run production evidence. Failure produces a `HOTFIX GA-XX.Y` based on the latest repo and no subsequent phase is authorized until PASS.
