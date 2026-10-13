## Scope and acceptance
- Goal / issue:
- What changed:
- Acceptance criteria satisfied:
- Files/areas touched:

## Architecture and compatibility
- [ ] Existing approved contracts, `schemaVersion: 4` and `syncContract: schema_version_4` preserved (or linked reviewed ADR)
- [ ] Tenant isolation / RBAC / audit considered
- [ ] Cash, payments, inventory and idempotency considered
- [ ] Offline SQLite and synchronization compatibility considered
- [ ] Migrations/backward compatibility addressed if applicable

## Verification for this exact head SHA
- Head commit SHA:
- GitHub Actions run URL:
- Windows .NET build and tests:
- PosDashboard build and self-test:
- Security and V1.1 static validations:
- PostgreSQL integration with disposable DB: executed / skipped / not applicable
- Browser E2E: executed / skipped / not applicable
- Physical WPF/peripheral validation: executed / pending / not applicable
- Known test skips, warnings, unverified cases:

## Release / authority gates
- [ ] No real external services, payment credentials or production customer data used for development
- [ ] No production migration, deployment, release, merge or infrastructure changes performed
- [ ] No new secrets or sensitive information included in code, logs, artifacts or PR
- [ ] Rollback or safe reversion described
- [ ] PO approval required before merge or deploy

## Risks and decisions
- Risks:
- Decisions / ADR links:
- Explicit PO action required:
- Next task:

> Keep this PR as **draft** until QA evidence and review are complete. CI success does not authorize merge or deployment.
