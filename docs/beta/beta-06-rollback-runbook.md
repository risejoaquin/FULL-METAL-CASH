# BETA-06 Rollback Runbook

The drill must not perform a destructive delete. It temporarily sets `revoked_at` on the promoted beta release inside a transaction, validates the rollback path, and executes SQL `ROLLBACK`. The final state must show `revoked_at IS NULL` and `persistedRollbackMutationCount = 0`.

A real rollback requires operator evidence and change approval; this drill validates the path without leaving production mutated.
