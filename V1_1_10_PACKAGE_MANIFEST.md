# Package Manifest: SolidPOS V1.1-10 Crash Reporting & Safe Telemetry

- **Phase:** V1.1-10 Crash Reporting & Safe Telemetry
- **Baseline SHA:** `8f3e2467040e63b820ee2ed0e3a147d07d931db9`
- **Target Branch:** `main`
- **schemaVersion:** 4
- schemaVersion: 4
- **syncContract:** schema_version_4
- syncContract: schema_version_4
- **Database Migration Required:** NO (Embeds optional crash evidence into existing PostgreSQL JSONB `pos.terminals.device_health`)
- **Authoritative Invariants:** `schemaVersion = 4`, `syncContract = schema_version_4`, offline-first SQLite durability preserved, zero raw minidumps.
- **Privacy Filtering:** Multi-stage redaction in `CrashReportSanitizer` scrubbing connection strings, database URLs, Bearer tokens, JWTs, Railway tokens, API keys, private keys, passwords, credit card PANs, emails, and filesystem usernames.

## Modified and Added Files

1. `src/PosCore/SolidPOS.PosCore.Domain/CrashReport.cs`
   - Created `CrashReport` domain model (`CrashId`, `CorrelationId`, `TimestampUtc`, `ExceptionType`, `SanitizedMessage`, `SanitizedStackTrace`, `AppVersion`, `SchemaVersion = 4`, `SyncContract = "schema_version_4"`).
   - Created `TelemetryOptInPolicy` and `TelemetryOptInMode` (`OptedOut`, `LocalOnly`, `OptedIn`).

2. `src/PosCore/SolidPOS.PosCore.Application/Diagnostics/CrashReportSanitizer.cs`
   - Comprehensive regex-based privacy scrubber.
   - Truncates oversized messages and stack traces to bounded character limits.

3. `src/PosCore/SolidPOS.PosCore.Application/Diagnostics/CrashReportService.cs`
   - Provides local offline JSON persistence in `.runtime\crashes\`.
   - Bounded retention policy (`DefaultMaxRetainedReports = 20`, `DefaultMaxReportFileSizeBytes = 65536`).
   - Oldest-first (FIFO) automated report pruning.
   - Synchronous and asynchronous capture methods with strict defensive exception handling.

4. `src/PosCore/SolidPOS.PosCore.Wpf/App.xaml.cs`
   - Wired `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`.
   - Re-entrancy guard preventing recursive crash cascades.

5. `src/PosCore/SolidPOS.PosCore.Cli/Program.cs`
   - Added top-level try-catch exception safety boundary.
   - Captures crash reports safely and outputs sanitized messages to stderr.

6. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalDeviceHealthDto.cs`
   - Added optional `CrashReportEvidenceDto? CrashEvidence = null`.
   - Added optional `IReadOnlyList<CrashReportEvidenceDto>? RecentCrashes = null`.

7. `tests/SolidPOS.PosCore.UnitTests/Diagnostics/CrashReportingTests.cs`
   - 17 unit tests validating structured envelope, correlation, schemaVersion 4, syncContract schema_version_4, secret redactions, PAN redactions, path sanitization, offline persistence, failure isolation, bounded retention, oversized evidence truncation, opt-out/opt-in policy, and unhandled exception workflows.

8. `tests/SolidPOS.PosServer.UnitTests/Terminals/TerminalEnrollmentServiceTests.cs`
   - Unit test validating backward-compatible JSON serialization and round-trip of `TerminalDeviceHealthDto` with optional `CrashEvidence`.

9. `scripts/v1.1/validate-v1.1-10-crash-reporting-safe-telemetry.ps1`
   - Automated static and functional validation script supporting `-StaticOnly`.

10. `.github/workflows/solidpos-ci.yml`
    - Added V1.1-10 static contract validation step.

11. `SOLIDPOS_V1_1_10_CRASH_REPORTING_SAFE_TELEMETRY.md`
    - Technical documentation and scope specification.

12. `V1_1_10_PACKAGE_MANIFEST.md`
    - Package manifest.

13. `V1_1_10_VALIDATION_COMMANDS.md`
    - Step-by-step reproduction and verification commands.
