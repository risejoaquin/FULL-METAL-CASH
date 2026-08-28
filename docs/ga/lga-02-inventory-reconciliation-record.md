# LGA-02 Inventory Reconciliation Record

ING-CAFE-G negative stock is reconciled through a controlled inventory adjustment when -ApplyInventoryAdjustment is used. InventoryDecision ADJUSTED or RECONCILED requires zero negative stock or an applied inventory adjustment.

This is a production mutation and must be validated through the manifest.


## HOTFIX-03 Contract Alignment

LGA-02 inventory reconciliation uses the production inventory adjustment contract with adjustmentType `correction`, positive quantityDelta for negative stock recovery, and invariant numeric formatting.
