# GA-06 Promotion and Rollback Runbook

1. Revalidate GA-04 and GA-05 production evidence.
2. Confirm migration 019 exists in production.
3. Select one controlled compatible terminal and one outside-cohort terminal.
4. Create/reuse internal release using GA-05 artifact identity.
5. Confirm target row persisted before continuing.
6. Create/reuse beta release with identical URL/hash/signature.
7. Create/reuse stable release with identical URL/hash/signature.
8. Run target/outside/unscoped update checks.
9. Run rollback transaction drill by temporarily revoking the stable row inside a transaction and issuing `ROLLBACK`.
10. Confirm stable release remains active after drill.

No destructive delete is permitted. Release history is preserved. Public rollout remains disabled.
