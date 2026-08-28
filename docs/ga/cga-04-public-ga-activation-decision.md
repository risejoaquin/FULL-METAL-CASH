# CGA-04 — Public GA Activation Decision

CGA-04 is the final controlled rollout decision gate for Public GA. The validator does **not activate** Public GA. It records whether SolidPOS should keep Limited GA, authorize Public GA through a separate explicit change, or block Public GA until remediation.

## Supported decisions

- `KEEP_LIMITED_GA`: keep the production rollout limited and leave Public GA not activated.
- `AUTHORIZE_PUBLIC_GA`: record an authorization intent only. It requires explicit operator approval and a clean zero-baseline state; the validator still does not flip activation flags.
- `BLOCK_PUBLIC_GA`: formally block Public GA until capacity, database, sync and incident conditions are remediated.

## Current expected route

The expected route for this environment is `KEEP_LIMITED_GA` because CGA-03 accepted limited capacity, known sync conflict baseline, known dead letter baseline and waiting connection baseline. Public GA remains `NOT_ACTIVATED`.


## HOTFIX-01 — Sync contract schema version compatibility

CGA-04.1 accepts the production sync contract field `currentSchemaVersion` as the canonical schema version and falls back from legacy `schemaVersion` only for compatibility. This does not change backend contracts, does not activate Public GA, and preserves the requirement that the resolved schema version is 4 and `schema_version_4` remains the accepted sync contract.
