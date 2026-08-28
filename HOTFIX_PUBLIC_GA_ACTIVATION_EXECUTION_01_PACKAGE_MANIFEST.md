# Hotfix Public GA Activation Execution 01

Changed:
- scripts/ga/public-ga-activation-execute.sql
- scripts/ga/public-ga-activation-rollback.sql
- scripts/ga/validate-public-ga-activation-execution.ps1
- SOLIDPOS_PUBLIC_GA_ACTIVATION_EXECUTION_HOTFIX_01_SAFE_SQL_GUARDS.md

Purpose: replace unsafe division-by-zero SQL assertions with deterministic psql guards.
