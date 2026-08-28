# GA-11 — Operator Acceptance Checklist

## Operator acceptance

- [ ] Sales read workflow validated.
- [ ] Catalog runtime validated.
- [ ] Inventory stock read validated.
- [ ] Dashboard overview validated.
- [ ] Cash shift operational state captured.
- [ ] Offline and sync readiness validated through sync status and sync contract.
- [ ] `schemaVersion=4` and `syncContract=schema_version_4` confirmed.
- [ ] No pending sync conflicts.
- [ ] Capacity condition is carried forward to GA-12.

## Operator conditions

Open shifts, absent customers, or absent active digital receipts may be recorded as launch-attention conditions if they do not break the acceptance blocker matrix.
