# SolidPOS V1.1-07 Offline Diagnostics & Support Bundle

## Diagnostics and Support Bundle Contract

V1.1-07 introduces a self-contained offline diagnostic bundle export mechanism for SolidPOS PosCore to reconstruct operational state without network access, production database migrations, or remote service calls.

Required behavior:

- Offline diagnostic bundle export generates a machine-readable support bundle containing operational diagnostics.
- Bundle format:
  support-bundle/
    manifest.json
    runtime.json
    sqlite.json
    sync.json
    hardware.json
    logs/
      sanitized-*.log
- Manifest metadata: bundleFormatVersion, generatedAtUtc, applicationVersion, includedSections, sanitization markers.
- Runtime diagnostics: application version, runtime version, schemaVersion 4, syncContract schema_version_4.
- SQLite diagnostics: availability, sanitized database path, integrity report, schema compatibility, WAL/journal/shm presence without exporting SQLite database table contents.
- Sync diagnostics: queue summary (PendingCount, ProcessingCount, RetryPendingCount, DeadLetterCount, OldestPendingAtUtc, OldestRetryPendingAtUtc, HasStuckProcessing, RequiresRecovery) and last sync timestamps (lastSuccessfulPullUtc, lastSuccessfulPushUtc).
- Hardware diagnostics: printer status, cash drawer status, terminal diagnostics without sensitive tokens, and local hardware event summary.
- Sanitized logs: technical diagnostic log entries preserved while defensive sanitization removes secrets, tokens, passwords, connection strings, private keys, provision keys, and customer/payment PII.

## Preserved Guarantees

- schemaVersion: 4
- syncContract: schema_version_4
- Zero mutation of operational data during bundle export.
- Bundle export operates completely offline without restarting PosCore.
- No secrets or sensitive PII leaked in diagnostics or logs.

## Operational Reconstruction

Support and engineering teams can reconstruct basic terminal operational state offline by inspecting:
- Local database health, schema compatibility, and WAL journal status.
- Sync queue health, pending/retry backlog, and stuck processing indicators.
- Hardware queue status, failed print jobs, and device operational events.
- Technical logs with technical context intact and sensitive credentials redacted.
