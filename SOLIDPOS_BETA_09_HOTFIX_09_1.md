# SolidPOS BETA-09 HOTFIX 09.1 — Stale validation cash shift triage

## Failure fixed
BETA-09 correctly blocked on `stale_open_shift_requires_review_before_closure` after inventory reconciliation succeeded.

## Decision
The blocker must not be weakened. Before final reconciliation, BETA-09 now classifies stale open shifts by terminal ownership:

- fingerprints `pilot-*`, `exp-*`, `iteration-*`, or `beta-*` are validation-owned fixtures;
- any other stale shift is treated as a real/operator shift and remains a hard blocker requiring operator evidence;
- validation-owned stale fixtures are closed with `counted_cash_cents = expected_cash_cents`, zero difference, and an explicit audit event;
- no real/operator shift is force-closed.

## Added
- `scripts/beta/beta-09-stale-validation-shift-triage.sql`
- `scripts/beta/beta-09-close-stale-validation-shifts.sql`

## Contract retained
- inventory ledger remains source of truth;
- final BETA-09 still requires `staleOpenShiftCount = 0`;
- schemaVersion = 4;
- syncContract = schema_version_4;
- final gate remains `PASS ... / GO BETA-10`.
