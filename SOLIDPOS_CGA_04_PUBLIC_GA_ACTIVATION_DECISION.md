# SolidPOS CGA-04 — Public GA Activation Decision

CGA-04 records the formal Public GA activation decision. The default and expected current decision is `KEEP_LIMITED_GA`.

The validator must not activate Public GA. It confirms production health, controlled rollout baselines and activation flags. Public GA remains `NOT_ACTIVATED`.

Expected status:

```text
PASS CGA-04 KEEP LIMITED GA / PUBLIC GA NOT ACTIVATED
```


## HOTFIX-01 — Sync contract schema version compatibility

CGA-04.1 accepts the production sync contract field `currentSchemaVersion` as the canonical schema version and falls back from legacy `schemaVersion` only for compatibility. This does not change backend contracts, does not activate Public GA, and preserves the requirement that the resolved schema version is 4 and `schema_version_4` remains the accepted sync contract.
