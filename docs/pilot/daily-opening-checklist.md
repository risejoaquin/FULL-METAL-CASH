# SolidPOS Pilot Daily Opening Checklist

Use this checklist before operating the pilot store each day.

## Before opening

- Confirm Railway service is deployed and stable.
- Confirm `/health/live` returns `alive`.
- Confirm `/health/ready` returns `ready`.
- Confirm Supabase database is available.
- Confirm admin login works.
- Confirm dashboard opens and shows Overview, Reports, Operations, Audit.
- Confirm active store is `MAIN`.
- Confirm active product `QSR-AMERICANO` exists.
- Confirm cash payment method exists.
- Confirm at least one active terminal exists.
- Confirm opening cash amount is counted.
- Confirm rollback owner is available.

## GO / NO-GO

Opening status:

```text
GO / NO-GO:
Reason:
Operator:
Time:
```

Do not process pilot sales if status is `NO-GO`.
