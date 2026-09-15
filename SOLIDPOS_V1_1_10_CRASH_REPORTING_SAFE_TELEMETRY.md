# SolidPOS V1.1-10: Crash Reporting & Safe Telemetry

## Overview
Phase V1.1-10 establishes a robust, offline-first crash capture and privacy-scrubbed telemetry subsystem across the SolidPOS platform. It ensures fatal and non-fatal runtime exceptions are captured in structured, correlation-tagged JSON envelopes with multi-stage sanitization (zero secrets / zero unnecessary PII), protects terminal storage via bounded retention, and respects explicit opt-in policy for remote operational telemetry.

## Architectural Invariants
- `schemaVersion: 4` remains authoritative and unchanged.
- `syncContract: schema_version_4` remains authoritative and unchanged.
- No database migrations: no new PostgreSQL tables or columns; telemetry reuses the existing `pos.terminals.device_health` JSONB column (migration 021) via `TerminalDeviceHealthDto`.
- Failure isolation: `TELEMETRY FAILURE != POS OPERATION FAILURE`. Crash capture and telemetry failures are strictly isolated and never disrupt offline checkout, cash shifts, receipt printing, or SQLite transactions.
- Offline-first resilience: all crash evidence is persisted locally to `.runtime\crashes` without network dependency.
- Zero raw memory dumps: no unmanaged `.dmp` / minidump files are created, preventing memory-resident secret leaks.

## Implemented Scope

### 1. Structured Crash Capture
- Created `CrashReport` domain model (`SolidPOS.PosCore.Domain`) with core correlation and diagnostic metadata:
  - `CrashId`: unique GUID identifying the fault instance.
  - `CorrelationId`: tracing identifier (`crash-{Guid:N}` or contextual correlation ID) linking to active sessions and requests.
  - `TimestampUtc`: standardized UTC event timestamp.
  - `ExceptionType`: fully qualified exception class name.
  - `SanitizedMessage`: scrubbed exception summary.
  - `SanitizedStackTrace`: scrubbed execution stack trace.
  - `AppVersion`, `SchemaVersion: 4`, `SyncContract: "schema_version_4"`.
  - Contextual GUID identifiers (`TerminalId`, `TenantId`, `StoreId`, `SessionId`) when already safely available in memory.
  - Excludes business payloads: zero cart items, payments, customer objects, outbox rows, or database queries.

### 2. Dedicated Crash Sanitizer
- Built `CrashReportSanitizer` (`SolidPOS.PosCore.Application.Diagnostics`) enforcing strict privacy filtering:
  - Redacts PostgreSQL connection URLs and database endpoints.
  - Redacts Npgsql and generic database connection strings (`Password=[REDACTED]`, `User Id=[REDACTED]`).
  - Redacts authentication tokens (Bearer tokens, standard JWTs, Supabase JWTs, Railway tokens).
  - Redacts keys (AWS access keys, API keys, provisioning/bootstrap keys, JWT signing keys, private keys).
  - Redacts passwords in assignments and JSON properties.
  - Redacts customer credit card PANs (13–19 digits).
  - Redacts email addresses.
  - Redacts developer and operator usernames embedded in Windows (`\Users\<user>`) and Unix (`/home/<user>`) filesystem paths.
  - Enforces hard character length limits on messages and stack traces to guard against denial-of-service via massive exception strings.

### 3. PosCore WPF Unhandled Exception Boundaries
- Wired global application-level exception handlers in `App.xaml.cs`:
  - `DispatcherUnhandledException`: captures UI thread exceptions, formats sanitized crash report, writes synchronous local artifact, and preserves normal termination semantics without unsafe continuation.
  - `AppDomain.CurrentDomain.UnhandledException`: captures fatal process exceptions synchronously before CLR termination.
  - `TaskScheduler.UnobservedTaskException`: captures unobserved background task faults and marks observed to prevent unhandled runtime escalation.
  - Re-entrancy protection: atomic flag prevents recursive crash loops if failure occurs within diagnostic routines.

### 4. PosCore CLI Exception Safety Boundary
- Wrapped CLI root execution (`Program.cs`) in a top-level defensive try-catch boundary.
- Captures command faults safely into `CrashReportService`, writes sanitized summary to stderr, and preserves standard exit code semantics (exit 1 on unhandled fault).

### 5. Local Crash Persistence & Bounded Retention
- Persists structured crash reports in `.runtime\crashes\crash-{yyyyMMddHHmmssfff}-{CrashId}.json`.
- Operates 100% offline without third-party SaaS services (no Sentry, Datadog, or external agent).
- Bounded retention policy (implementation policy):
  - `DefaultMaxRetainedReports`: 20 files.
  - `DefaultMaxReportFileSizeBytes`: 64 KB per report.
  - Oldest-first (FIFO) pruning automatically purges excess files to protect local terminal storage.
  - All file I/O operations are wrapped in defensive handlers: persistence failure never throws to the caller.

### 6. Operational Telemetry Opt-In Policy
- Created `TelemetryOptInPolicy` and `TelemetryOptInMode`:
  - `OptedOut`: remote transmission prohibited; local diagnostic capture preserved for on-site support.
  - `LocalOnly`: local diagnostic capture preserved; remote telemetry disabled.
  - `OptedIn`: remote telemetry transmission permitted only if remote `DiagnosticsEnabled` is also true.
- Clear separation: local crash logging is always available for terminal supportability, whereas remote operational telemetry strictly obeys operator consent.

### 7. PosServer Contract Extension
- Extended `TerminalDeviceHealthDto` with optional `CrashReportEvidenceDto? CrashEvidence` and `IReadOnlyList<CrashReportEvidenceDto>? RecentCrashes`.
- Backward-compatible contract: default null, no breaking API change, seamlessly serializes into existing PostgreSQL JSONB `pos.terminals.device_health`.
- No database migration required.
