# LGA-03 HOTFIX-02 — Dashboard Overview Endpoint Contract Alignment

## Decision

LGA-03 HOTFIX-02 aligns the Limited GA multi-day stability burn-in validator with the production dashboard overview API contract.

## Issue

The LGA-03.1 validator called `GET /api/v1/dashboard/overview`, which is not exposed by the production PosServer contract and returns HTTP 404. Earlier LGA/CGA gates use the reports dashboard contract instead.

## Fix

The validator now calls:

```text
GET /api/v1/reports/dashboard/overview?from=...&to=...&limit=20&trendBucket=day
```

## Scope

Changed:

- `scripts/ga/validate-lga-03-limited-ga-multi-day-stability-burn-in.ps1`
- LGA-03 command and summary documentation

Not changed:

- PosServer runtime
- PosCore WPF
- PosCore CLI
- PosDashboard runtime
- migrations
- database state
- sync contract
- Public GA activation

## Expected outcome

The Day 1 burn-in checkpoint must proceed past dashboard overview API validation and continue to the database snapshot and blocker matrix.

Public GA remains `NOT_ACTIVATED`.
