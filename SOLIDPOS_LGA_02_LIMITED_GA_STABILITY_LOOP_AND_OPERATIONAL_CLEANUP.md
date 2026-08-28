# SOLIDPOS LGA-02 — Limited GA Stability Loop and Operational Cleanup

## Scope

LGA-02 validates a new Limited GA sales cycle after LGA-01 and performs operational cleanup.

## Objectives

1. Repeat real sales cycle in Limited GA.
2. Confirm conflictCount does not rise above 3.
3. Confirm deadLetterCount does not rise above 1.
4. Adjust or reconcile ING-CAFE-G negative stock.
5. Confirm WPF QSR command enablement visually.
6. Keep Public GA disabled.

## Expected status

PASS LGA-02 LIMITED GA STABILITY LOOP AND OPERATIONAL CLEANUP / GO LGA-03.


## LGA-02-HOTFIX-01 — Real sales cycle document contract alignment

Aligned `docs/ga/lga-02-real-sales-cycle-record.md` with validator guardrails by explicitly including the required `stability loop` term. No backend, WPF, database migration, or Public GA activation changes.


## LGA-02-HOTFIX-02 — WPF visual confirmation document contract alignment

Aligned `docs/ga/lga-02-wpf-qsr-visual-confirmation.md` with validator-required terms: WPF, QSR, visual confirmation, and cash payment. This is a documentation/validator contract fix only; it does not change PosServer, PosCore runtime logic, database migrations, sync contract, or Public GA activation.


## LGA-02-HOTFIX-03 — Inventory Adjustment Contract Alignment

Aligned the validator inventory adjustment payload with the production API by using `adjustmentType = correction` and invariant `quantityDelta`. Public GA remains NOT_ACTIVATED.
