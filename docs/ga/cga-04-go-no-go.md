# CGA-04 Go / No-Go

## GO: keep limited GA

Use `KEEP_LIMITED_GA` when controlled rollout is healthy but Public GA should remain not activated.

## AUTHORIZE PUBLIC GA

Use `AUTHORIZE_PUBLIC_GA` only with explicit approval and zero accepted baselines for sync conflicts, dead letters and waiting connections. The validator does not activate Public GA flags.

## NO_GO / BLOCK PUBLIC GA

Use `BLOCK_PUBLIC_GA` when capacity, database, sync, incident, RLS or financial integrity conditions require remediation.

Public GA must remain not activated unless a separate explicit deployment/configuration change is authorized.


## HOTFIX-01 — Sync contract schema version compatibility

CGA-04.1 accepts the production sync contract field `currentSchemaVersion` as the canonical schema version and falls back from legacy `schemaVersion` only for compatibility. This does not change backend contracts, does not activate Public GA, and preserves the requirement that the resolved schema version is 4 and `schema_version_4` remains the accepted sync contract.
