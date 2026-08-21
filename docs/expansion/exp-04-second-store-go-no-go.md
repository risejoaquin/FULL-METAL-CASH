# EXP-04 Second Store GO/NO-GO Matrix

## GO
- Build PASS.
- Tests PASS.
- Secret scan PASS.
- Health/readiness PASS.
- Admin login PASS.
- Second store active.
- Admin store access present.
- Initial terminal active.
- Bootstrap store/terminal isolation PASS.
- Controlled sale completed in the second store.
- Digital receipt active.
- Cash shift closed with zero difference.
- Store-filtered sales read model PASS.
- Dashboard monitoring PASS.
- Audit evidence PASS.
- SQL cross-check PASS.
- Terminal dead letters = 0.
- Terminal pending conflicts = 0.

## NO-GO blockers
- Store missing or inactive.
- Terminal missing or inactive.
- Sale attached to MAIN instead of the second store.
- Missing payment, receipt, inventory ledger, or audit evidence.
- Cash difference not zero.
- Terminal dead letter > 0.
- Terminal pending conflict > 0.

## Conditions carried to EXP-05
- monitor_retry_pending_sync
- triage_known_dead_letter
- inventory_reconciliation_required

Next phase after GO: EXP-05 Operational Monitoring Hardening.
