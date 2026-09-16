# Package Manifest: SolidPOS V1.1-11 PosDashboard Operations Center

- **Phase:** V1.1-11 PosDashboard Operations Center
- **Baseline SHA:** `5557add2562759ff429a0df2c4fd0785ea1d1b48`
- **Target Branch:** `lucilferChanges/fastandrun`
- **schemaVersion:** 4
- schemaVersion: 4
- **syncContract:** schema_version_4
- syncContract: schema_version_4
- **Database Migration Required:** NO
- **PosServer Modification Required:** NO
- **New NPM Dependencies:** 0
- **New NuGet Dependencies:** 0
- **Lockfile Restoration Notice:** `src/PosDashboard/SolidPOS.PosDashboard.Admin/package-lock.json` was restored from historical commit `322fef5c222c7fa536c7f1faea8f22209e552eeb` only after verifying byte-for-byte parity of `package.json` (SHA256: `9168F6CB4C37B4A36AB1CCC6AE48CCA74FD039606C77A736C8F13FEDA1C11FEE`), to restore deterministic `npm ci` behavior without unconstrained dependency drift. It was NOT newly generated.

---

## Restored and Modified Files

1. `src/PosDashboard/SolidPOS.PosDashboard.Admin/package-lock.json`
   - Restored from commit `322fef5c222c7fa536c7f1faea8f22209e552eeb` (SHA256: `EBDE214FB907BBF02B317FA7350BBD46DB201606117FA8A2A81181AC93A4AE34`).
   - Enables hermetic and deterministic `npm ci`.

2. `src/PosDashboard/SolidPOS.PosDashboard.Admin/src/api/posServerClient.ts`
   - Added TypeScript types: `ReadinessResponse`, `ReadinessDependencyDto`, `ProductionAlertsResponse`, `ProductionAlertResponse`, `TerminalResponse`, `UpdateHealthEvidenceDto`, `CrashReportEvidenceDto`, `TerminalDeviceHealthDto`, `SyncStatusBucketDto`, `VersionAdoptionSummary`, `IncidentSummary`.
   - Updated `SyncStatusDto` with fields from `SyncRuntimeStatusResponse` (`processingCount`, `retryPendingCount`, `oldestPendingAt`, `lastProcessedAt`, `buckets`).
   - Added helper `hasPermission(session, permission)` and `PermissionCodes`.
   - Added methods: `getHealthReadiness()`, `getAlerts(accessToken)`, `getTerminals(accessToken)`, `getTerminalHealth(accessToken, terminalId)`.
   - Updated `getOperationsSnapshot()` to execute with `Promise.allSettled()` for partial failure isolation.

3. `src/PosDashboard/SolidPOS.PosDashboard.Admin/src/features/dashboard/OperationsDashboard.tsx`
   - Rendered the seven official operational surfaces:
     - 1. API Health: readiness status, DB latency, schemaVersion (4), syncContract (`schema_version_4`), schema compatibility, storage readiness.
     - 2. DB Health: PostgreSQL status, active connection count, missing tables, server version.
     - 3. Sync Queues: Pending, Processing, Processed, RetryPending, DeadLetter, OldestPendingAt timestamp, recovery status.
     - 4. Terminal Status: Fleet terminal table (Name, Status, Version, Last-seen), with RBAC permission indicator for `terminals.manage`.
     - 5. Incidents: Operational incident summary banner displaying active critical/warning incidents derived from alerts, dead-letters, and sync backlogs.
     - 6. Version Adoption: Fleet version distribution (count and percentage per version) and server schema version.
     - 7. Alert Summary: Evaluated production alerts table showing code, severity, active state, observed vs threshold values, and descriptions.

4. `src/PosDashboard/SolidPOS.PosDashboard.Admin/src/features/dashboard/DashboardHome.tsx`
   - Replaced static `"Iteration 20"` badge with live schema version from `snapshot?.versionAdoption?.serverVersion`.
   - Passed `session` prop to `OperationsDashboard` for RBAC permission checks.

5. `src/PosDashboard/SolidPOS.PosDashboard.Admin/scripts/self-test.mjs`
   - Added static assertions for V1.1-11 endpoints, types, RBAC permission helpers, and all 7 operations surfaces.

6. `.github/workflows/solidpos-ci.yml`
   - Added Node.js 22 setup action (`actions/setup-node@v4`).
   - Added PosDashboard verification step (`npm ci`, `npm run self-test`, `npm run build`).
   - Added V1.1-11 static validator execution step.

7. `scripts/v1.1/validate-v1.1-11-posdashboard-operations-center.ps1`
   - Automated PowerShell validator verifying the 7 capabilities, safe model consumption, no Prometheus scraping, RBAC handling, zero migrations, schemaVersion 4, syncContract schema_version_4, lockfile presence, and zero PosServer changes. Supports `-StaticOnly`.

8. `SOLIDPOS_V1_1_11_POSDASHBOARD_OPERATIONS_CENTER.md`
   - Technical design documentation.

9. `V1_1_11_PACKAGE_MANIFEST.md`
   - This package manifest.

10. `V1_1_11_VALIDATION_COMMANDS.md`
    - Step-by-step reproduction and verification commands.
