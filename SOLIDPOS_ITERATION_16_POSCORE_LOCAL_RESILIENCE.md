# SolidPOS Iteration 16 — PosCore Local Resilience / Recovery / Data Integrity

## Scope

This iteration hardens PosCore local runtime before expanding the WPF surface.

Implemented:

- SQLite `PRAGMA integrity_check` diagnostic.
- Local recovery journal.
- Local backup command.
- Controlled recovery for failed outbox events.
- Controlled recovery for failed print jobs.
- Expired active-session cleanup.
- Cash-shift anomaly detection.
- CLI diagnostics for operators.
- Unit tests for `LocalResilienceService`.
- PowerShell validation script.

## New CLI commands

```text
verify-local-integrity
backup-local-db
repair-local-runtime
recovery-journal
seed-resilience-fixture
```

`seed-resilience-fixture` exists for deterministic validation only. Production operators use diagnostics, backup, repair, and journal commands.

## Tables

```text
local_recovery_journal
```

## Architectural decision

Recovery is local-first and explicit. The runtime does not silently mutate failed queues. Operator-visible repair writes a journal entry and can create a backup before applying controlled corrections.

## Validation

See `ITERATION_16_VALIDATION_COMMANDS.md`.
