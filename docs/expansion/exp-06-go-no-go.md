# EXP-06 GO / NO-GO

## GO

EXP-06 can close as GO when:

- negative inventory after reconciliation is zero;
- pending conflicts are zero;
- modifier inventory semantics are valid;
- substitute modifiers include `replaces_product_id`;
- recipe items are valid;
- ledger reconciliation evidence exists when negative inventory existed before;
- build/test/secret scan pass;
- health/readiness pass.

## NO-GO

EXP-06 is NO-GO when:

- negative inventory remains after reconciliation;
- pending conflicts exist;
- active admin user is missing;
- inventory tables are missing;
- modifier or recipe semantics are invalid;
- schema version is not 4.

## Next

GO authorizes EXP-07 — Sync SLA and Offline Reliability Hardening.
