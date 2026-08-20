# PILOT-03 Operator Checklist

## Before running

- PILOT-01 must be GO.
- PILOT-02 must be GO.
- Railway production must be ready.
- Supabase DATABASE_URL must point to production.
- Admin password must be available as SecureString.

## Validation

Run `scripts/pilot/validate-cash-drawer-shift-operations.ps1`.

## Expected outcome

- Cash shift opened.
- Cash in created.
- Cash out created.
- Drawer open without sale created.
- Two cash sales created.
- Shift closed with difference zero.
- SQL assertion returns GO.

## After running

Review:

```text
docs/pilot/logs/pilot-03-cash-shift-log.md
```
