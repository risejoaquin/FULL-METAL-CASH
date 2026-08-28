# EXP-09 — Release Smoke Test Checklist

## Required smoke test
- Production `/health/live` returns alive.
- Production `/health/ready` returns ready and database ready.
- Admin login returns access token.
- `/api/v1/updates/channels` returns stable, beta, and internal channels.
- `/api/v1/updates/releases` creates tenant-scoped non-mandatory internal release.
- `/api/v1/updates/check` returns update decision for the created package.
- SQL cross-check confirms release row, rollback version, artifact hash, signature, universal installer, and no revoke.

## Failure rule
Any failed smoke test is NO-GO for EXP-10.
