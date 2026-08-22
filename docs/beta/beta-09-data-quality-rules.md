# BETA-09 Data Quality Rules

Mandatory closure rules:

- no negative price
- no invalid price window
- no invalid tax mode
- no invalid modifier behavior
- substitute modifier requires replaces_product_id and valid consumption linkage
- no untriaged new dead-letter
- no unresolved conflicts
- cash differences reviewed
- open shifts reviewed
- no negative inventory after reconciliation
- no sales/payment reconciliation mismatch
- no return/refund reconciliation mismatch
- no orphan active receipt
- no legacy sync schema event
- active beta release must preserve Velopack/signature/rollback contract

The `inventory_ledger` remains the inventory source of truth. Reconciliation must append evidence rather than rewrite historical ledger rows.
