# Package Manifest — Public GA Activation Execution

Added:
- `scripts/ga/validate-public-ga-activation-execution.ps1`
- `scripts/ga/public-ga-activation-state-check.sql`
- `scripts/ga/public-ga-activation-execute.sql`
- `scripts/ga/public-ga-activation-rollback.sql`
- `scripts/ga/rollback-public-ga-activation.ps1`
- `docs/ga/public-ga-activation-execution.md`
- `docs/ga/public-ga-activation-rollback-runbook.md`
- `docs/ga/public-ga-activation-execution-checklist.md`
- `PUBLIC_GA_ACTIVATION_EXECUTION_VALIDATION_COMMANDS.md`
- `PUBLIC_GA_ACTIVATION_EXECUTION_PACKAGE_MANIFEST.md`
- `SOLIDPOS_PUBLIC_GA_ACTIVATION_EXECUTION.md`

No schemaVersion or sync-contract change. Public GA activation is persisted in existing `pos.tenant_configs.feature_flags`.
