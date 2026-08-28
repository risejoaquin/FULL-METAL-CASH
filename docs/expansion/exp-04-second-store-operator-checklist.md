# EXP-04 Second Store Operator Checklist

## Before
- Confirm EXP-03 is PASS.
- Confirm production health/live and health/ready are passing.
- Confirm admin login works.
- Confirm DATABASE_URL points to PostgreSQL/Supabase.
- Confirm product QSR-AMERICANO and cash payment method exist.

## During
- Create second store.
- Register initial terminal.
- Verify bootstrap returns the new storeId and terminalId.
- Open cash shift.
- Create controlled sale.
- Issue digital receipt.
- Close cash shift with zero difference.

## After
- Capture terminalId, storeId, shiftId, saleId, receiptId.
- Verify terminal dead letters are zero.
- Verify terminal pending conflicts are zero.
- Verify store sale appears only under the second store.
- Save evidence in the manifest and log.
