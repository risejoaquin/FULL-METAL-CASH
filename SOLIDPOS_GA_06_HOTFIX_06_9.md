# HOTFIX GA-06.9 — End-to-End Contract Hardening

## Scope

This hotfix is the consolidated audit/hardening pass for GA-06 after repeated failures caused by contract assumptions differing across PowerShell, API, service, repository, PostgreSQL and audit evidence.

## Fixed families

- Explicit service-layer release validation diagnostics (`INVALID_RELEASE_REQUEST`) instead of generic null/409.
- Explicit repository write rejection (`RELEASE_WRITE_REJECTED`) instead of generic null/409.
- Idempotent audit semantics distinguish `updates.release.created` from `updates.release.reconciled`.
- `updates.release.cohort.targeted` is emitted only when target rows were actually inserted.
- SemVer-aware fail-closed update check prevents downgrade offers to clients already newer than the candidate.
- GA-06 validator emits a safe request-shape diagnostic before release POST; no secrets or full signature/hash values are printed.
- Per-channel cohort target checks require exactly one target for internal, beta and stable.
- SQL gates detect target tenant mismatches between release, target row and terminal.
- Final audit gates count distinct release ids rather than retry-generated event volume.
- Final artifact identity gate includes rollback version, mandatory, universal installer and tenant scope.

## Safety

No destructive data cleanup, no release overwrite, no target deletion and no automatic public rollout were added.
`schemaVersion = 4`, `syncContract = schema_version_4`, `mandatory = false`, and `generalAvailabilityActivated = false` remain invariant.
