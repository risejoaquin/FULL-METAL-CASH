# Public GA Activation Decision - Validation Commands

Run from repository root after the Public GA Readiness Review has been formally reviewed as PASS.

Use `Read-Host -AsSecureString` for the admin password and `Read-Host` for `DATABASE_URL`; never paste secrets into logs.

Validator: `scripts\ga\validate-public-ga-activation-decision.ps1`

For the reviewed PASS prerequisite, use `-SkipPublicGaReadinessReviewRevalidation` only when the prior production PASS logs have already been reviewed.
