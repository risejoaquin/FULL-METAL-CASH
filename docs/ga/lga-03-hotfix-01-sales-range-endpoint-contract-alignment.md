# LGA-03-HOTFIX-01 — Sales Range Endpoint Contract Alignment

## Scope
This hotfix aligns the LGA-03 burn-in validator with the existing SolidPOS production API contract.

## Problem
The original LGA-03 validator called `GET /api/v1/reports/sales?from=&to=`, which returned `404` in production because the validated contract uses `GET /api/v1/reports/sales/range?from=&to=`.

## Decision
Use the established sales range read model endpoint already validated by LGA-01, LGA-02, CGA, and GA validators.

## Changes
- Validator version updated to `LGA-03.1-sales-range-endpoint-contract-alignment`.
- Sales range call changed to `/api/v1/reports/sales/range`.
- No backend, database, WPF, dashboard, sync contract, or Public GA activation changes.

## Expected result
LGA-03 Day 1 should continue past API checks and evaluate the burn-in blocker matrix using the real production counters.

## Public GA
Public GA remains `NOT_ACTIVATED`.
