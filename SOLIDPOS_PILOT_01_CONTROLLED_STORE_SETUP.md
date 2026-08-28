# SolidPOS PILOT-01 — Controlled Store Pilot Setup

## Status

`PENDING USER VALIDATION`

## Goal

Prepare the production tenant/store for controlled pilot operation. This phase validates operational readiness before processing real controlled POS transactions.

## Added artifacts

- `scripts/pilot/validate-controlled-store-pilot-setup.ps1`
- `scripts/pilot/pilot-01-store-setup-check.sql`
- `docs/pilot/controlled-store-pilot-setup.md`
- `docs/pilot/daily-opening-checklist.md`
- `docs/pilot/daily-closing-checklist.md`
- `docs/pilot/pilot-rollback-plan.md`
- `docs/pilot/pilot-01-operator-brief.md`
- `PILOT_01_VALIDATION_COMMANDS.md`
- `SOLIDPOS_PILOT_01_CONTROLLED_STORE_SETUP.md`

## Production objects validated

- Active tenant.
- Active store.
- Active unlocked admin user.
- Admin store access.
- Active sellable pilot product.
- Positive product price.
- Active cash payment method.
- Active POS terminal for pilot store.
- Sales read model availability.
- Audit event read model availability.
- Dashboard production build.
- Railway/Supabase readiness.

## Architectural decision

PILOT-01 does not add new product features. It adds operational validation, pilot checklists, and rollback discipline around the existing production-ready SolidPOS runtime.

## Risk

This phase touches production validation. It must not expose credentials in logs, commits, chat, or generated artifacts. `DATABASE_URL` and admin password must be entered locally using PowerShell prompts or environment variables.

## Completion criteria

PILOT-01 is complete only when the validation script returns:

```text
controlledStoreSetup : passed
goNoGo               : GO
message              : SolidPOS PILOT-01 controlled store pilot setup completed.
```
