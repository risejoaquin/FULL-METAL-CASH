# CGA-04 Evidence Matrix

| Evidence area | Required result |
| --- | --- |
| CGA-03 entry gate | PASS CGA-03 FORMAL LIMITED CAPACITY ACCEPTANCE / GO CGA-04 |
| Public GA flags | Not activated |
| Capacity | Within limited accepted threshold |
| Database | Within accepted waiting connection baseline |
| Sync | Pending/retry/processing zero; known conflict baseline not exceeded |
| Dead letters | Known baseline not exceeded |
| RLS | No tenant tables missing RLS |
| Dashboard | Correct dashboard overview contract |
| Decision | KEEP_LIMITED_GA, AUTHORIZE_PUBLIC_GA, or BLOCK_PUBLIC_GA |


## HOTFIX-01 — Sync contract schema version compatibility

CGA-04.1 accepts the production sync contract field `currentSchemaVersion` as the canonical schema version and falls back from legacy `schemaVersion` only for compatibility. This does not change backend contracts, does not activate Public GA, and preserves the requirement that the resolved schema version is 4 and `schema_version_4` remains the accepted sync contract.
