# SolidPOS EXP-04 — Second Store Limited Expansion

Status: PENDING USER VALIDATION

## Objective
Validate a real limited expansion from one production store to a second production store without breaking the existing MAIN store.

## Scope
- Create one active second store.
- Assign admin store access for the new store.
- Register one initial terminal for the second store.
- Execute bootstrap sync and confirm store/terminal isolation.
- Open an independent cash shift in the second store.
- Create one controlled cash sale from the second store.
- Issue one digital receipt.
- Validate store-filtered sales read models.
- Validate dashboard monitoring, sync, conflicts, dead letters, audit evidence, and SQL cross-check.
- Produce GO/NO-GO evidence for EXP-05.

## Go/No-Go
GO only if second store, terminal, shift, sale, receipt, audit, sync and SQL validation pass with zero blockers.

## Rollback
If expansion fails, use the EXP-04 rollback runbook for containment. Do not delete production evidence unless explicitly approved.
