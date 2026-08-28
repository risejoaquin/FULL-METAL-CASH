# LGA-05 Operational Continuity Checklist

## operational continuity
- sales volume stays above minimum
- payments volume stays above minimum
- receipts are issued
- sync pending / processing / retry queues stay clear
- inventory has no negative stock regression
- open shifts remain controlled
- database waiting connections remain within accepted baseline
- Public GA remains not activated

## sales
Minimum completed sales in the 24 hour window must remain satisfied.

## sync
Schema version 4 and sync contract schema_version_4 are required.

## inventory
Negative stock count must remain zero.

## receipts
Receipts issued in the 24 hour window must remain above minimum.
