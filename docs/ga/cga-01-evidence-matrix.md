# CGA-01 Evidence Matrix

Required evidence:

- Repository/document guardrails PASS.
- Local build/test/secret guardrails PASS.
- Post-GA-12 prerequisite PASS or external manifest/log present.
- health/live PASS.
- health/ready PASS.
- observability protected PASS.
- tenant/stores/users/roles/permissions/catalog PASS.
- sales range PASS.
- dashboard overview PASS using `from`, `to`, `limit=20`, `trendBucket=day`.
- schemaVersion 4.
- syncContract schema_version_4.
- database controlled rollout snapshot PASS.
- no duplicate local sales.
- no pending conflicts.
- no stale processing.
- no RLS drift.
- Public GA: NOT ACTIVATED.
