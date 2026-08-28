# SolidPOS POST-LGA Capacity Hotfix 02 — Validator Execution Contract Restoration

This hotfix restores the full POST-LGA capacity validator execution flow that was accidentally removed while replacing the concurrency probe in Hotfix 01. The curl-based latency measurement remains in place so PowerShell `Start-Job` startup overhead is excluded from HTTP latency.

The hotfix does not change API contracts, database schema, readiness semantics, capacity thresholds, Limited GA scope, or Public GA activation state.
