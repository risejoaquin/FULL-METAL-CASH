# GA-06 End-to-End Audit — 2026-08-22

## Result

GA-06 was audited across PowerShell orchestration, GA-05 evidence ingestion, PostgreSQL preflight, terminal cohort selection, release creation/reconciliation, update checks, audit evidence, cohort SQL, rollback dry-run and final gate.

## Error families found

1. **Document literal contract mismatch** — fixed in GA-06.1.
2. **Migration target drift** — fixed in GA-06.2.
3. **Forward-incompatible historical migration constraint** — fixed in GA-06.3.
4. **Empty scalar converted to null method invocation** — fixed in GA-06.4.
5. **Collection wrapper treated as an item** — fixed in GA-06.5.
6. **Partial release persisted before cohort-aware backend deploy; retry was not reconciling it** — fixed in GA-06.6.
7. **SQL/API identity diagnostics were not aligned** — hardened in GA-06.7/06.8.
8. **Service-layer validation still collapsed multiple causes to generic null/409** — fixed in GA-06.9 with `INVALID_RELEASE_REQUEST` and explicit fields.
9. **Repository null write could still become generic 409** — fixed in GA-06.9 with `RELEASE_WRITE_REJECTED`.
10. **Idempotent retry produced inaccurate `updates.release.created` audit semantics** — fixed in GA-06.9 by distinguishing `created` vs `reconciled` and emitting `cohort.targeted` only for actual inserts.
11. **Update check used only version inequality and could offer a downgrade** — fixed in GA-06.9 with SemVer-aware fail-closed ordering.
12. **Per-channel target persistence was inferred from aggregate count** — fixed in GA-06.9 with exact internal/beta/stable target counts.
13. **Target tenant consistency depended only on repository code** — GA-06.9 adds SQL detection across release/target/terminal tenant ids.
14. **Audit gate could be inflated by retry event volume** — GA-06.9 counts distinct release ids with valid create/reconcile evidence.
15. **Final artifact identity gate omitted rollback/safety fields** — GA-06.9 includes rollback version, mandatory, universal installer and tenant scope.

## Intentionally unchanged

- schemaVersion = 4
- syncContract = schema_version_4
- mandatory = false
- tenantScoped = true
- publicRolloutAllowed = false
- generalAvailabilityActivated = false
- no destructive release cleanup
- no target deletion
- no automatic public rollout
