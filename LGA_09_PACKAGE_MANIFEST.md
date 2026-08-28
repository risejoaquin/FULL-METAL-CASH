# LGA-09 Package Manifest

## Phase

`LGA-09 — Limited GA Stability Confirmation / Capacity Risk Review`

## Base

Full repository package derived from the LGA-08 package that produced reviewed PASS real-production logs.

## New LGA-09 files

- `scripts/ga/validate-lga-09-limited-ga-stability-confirmation-capacity-risk-review.ps1`
- `scripts/ga/lga-09-limited-ga-stability-confirmation-capacity-risk-review-check.sql`
- `docs/ga/lga-09-limited-ga-stability-confirmation-capacity-risk-review.md`
- `docs/ga/lga-09-stability-confirmation-plan.md`
- `docs/ga/lga-09-capacity-risk-review-checklist.md`
- `docs/ga/lga-09-evidence-matrix.md`
- `LGA_09_VALIDATION_COMMANDS.md`
- `SOLIDPOS_LGA_09_LIMITED_GA_STABILITY_CONFIRMATION_CAPACITY_RISK_REVIEW.md`
- `LGA_09_PACKAGE_MANIFEST.md`

## Preserved guardrails

- Public GA cannot be activated by the LGA-09 validator.
- `PublicGaDecision` only accepts `KEEP_LIMITED_GA`.
- schema version 4 is mandatory.
- negative stock maximum remains 0.
- waiting connections maximum remains 12.
- accepted sync conflict baseline remains 3.
- accepted dead-letter baseline remains 1.
- capacity concurrency probe remains 3 requests wide with six requests by default.
- readiness/live p95 threshold remains 1200 ms.
- no automatic advancement to LGA-10.

## Current expected decision

If the capacity probe continues to fail, execute with:

`CAPACITY_UPGRADE_REQUIRED_BEFORE_PUBLIC_GA`

This keeps Limited GA operational under the formal capacity-risk acceptance while explicitly blocking a Public GA recommendation on current infrastructure.

## Validation status

Package construction and static contract checks completed. Real LGA-09 PASS must come from the user's production PowerShell execution and reviewed logs.
