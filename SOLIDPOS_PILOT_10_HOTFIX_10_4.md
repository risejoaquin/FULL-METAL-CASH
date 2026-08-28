# SolidPOS PILOT-10 HOTFIX 10.4 — SQL Required Table Contract

Status: PENDING USER VALIDATION

## Problem

PILOT-10 SQL cross-check returned NO-GO with reason `required_table_missing`.

The validator expected `pos.refunds`, but the real production schema validated by PILOT-04 uses `pos.return_refunds`.

## Fix

- Updated `scripts/pilot/pilot-10-production-expansion-check.sql` to require `pos.return_refunds` instead of `pos.refunds`.
- Added explicit missing-table reasons such as `missing_pos_return_refunds` to avoid opaque `required_table_missing` failures.

## Scope

No backend, dashboard, PosCore, migration, seed, or production data changes.
