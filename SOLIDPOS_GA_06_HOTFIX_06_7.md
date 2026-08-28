# HOTFIX GA-06.7 — Exact Release Identity Drift Diagnostics

## Cause
GA-06.6 correctly returned HTTP 409 when an existing release did not match the exact immutable release identity. The pre-promotion SQL gate did not include `rollback_version`, so it could report PASS before the backend rejected the same row.

## Changes
- `ga-06-release-chain-state.sql` now compares rollback version using `IS DISTINCT FROM`.
- The SQL emits per-channel identity diagnostics for artifact URL/hash, signature, rollback version, mandatory, universal installer, tenant scope and revoked state.
- The validator prints only field names and non-secret rollback values when drift is found, then hard-stops before POST.
- The selected cohort terminal is revalidated against PostgreSQL as active.

## Safety
No release is updated, revoked, deleted or retargeted by this diagnostic gate. Existing drift remains a blocker until explicitly classified.
