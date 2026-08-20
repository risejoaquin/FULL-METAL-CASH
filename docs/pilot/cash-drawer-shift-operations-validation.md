# Cash Drawer / Shift Operations Validation

This document defines the operational validation for SolidPOS PILOT-03.

PILOT-03 validates that a controlled store can operate a real cash shift in production with movement tracking, sale accumulation, closing count and auditability.

## Operator sequence

1. Confirm production readiness.
2. Authenticate admin.
3. Register pilot terminal.
4. Open cash shift.
5. Register cash in.
6. Register cash out.
7. Register no-sale drawer open.
8. Execute two controlled cash sales.
9. Confirm operational summary.
10. Close shift with counted cash equal to expected cash.
11. Confirm zero difference.
12. Confirm audit and SQL persistence.

## GO / NO-GO

GO requires zero discrepancy between expected and counted cash.

NO-GO if any of the following occurs:

- cash shift cannot open;
- movement is rejected;
- sales do not attach to the shift;
- summary does not match arithmetic;
- close fails;
- difference is non-zero;
- audit or SQL persistence is missing.
