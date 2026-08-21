# SolidPOS PILOT-10 Operator Checklist

## Pre-expansion

- Confirm PILOT-01 through PILOT-09 are PASS REAL PRODUCTION / GO.
- Confirm health live and ready are green.
- Confirm database readiness is green.
- Confirm admin login works.
- Confirm dashboard monitoring is available.
- Confirm incident runbook exists.
- Confirm rollback plan exists.
- Confirm pending conflicts are zero.
- Confirm failed payments in the last 24 hours are zero.

## During expansion

- Expand one store or terminal group at a time.
- Monitor failed requests.
- Monitor p95 latency.
- Monitor sync backlog.
- Monitor retry pending sync.
- Monitor dead letter sync.
- Monitor cash drawer differences.
- Monitor receipt generation.
- Monitor negative inventory.

## Post-expansion

- Review audit events.
- Confirm no pending conflicts.
- Confirm no failed payments.
- Confirm cash shifts reconcile.
- Confirm receipts remain available.
- Confirm backup and rollback references are still valid.

## Rollback trigger

Rollback when SEV1 appears, when payment integrity is uncertain, when cash reconciliation fails, or when database readiness fails.
