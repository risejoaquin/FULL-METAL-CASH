# SolidPOS V1.1-09: Update Channel & Velopack Hardening

## Overview
Phase V1.1-09 hardens the update delivery, channel governance, and deployment lifecycle across the SolidPOS platform (PosCore, PosServer, PosBuilder), providing deterministic update safety, cohort targeting, Authenticode signing enforcement, rollback package integrity verification, and sanitized update health evidence.

## Architectural Invariants
- `schemaVersion: 4` remains authoritative and unchanged.
- `syncContract: schema_version_4` remains authoritative and unchanged.
- Upgrade and rollback flows must be deterministic, reproducible, and verifiable.
- Database and client compatibility is strictly preserved across updates.
- No database migrations required: update health telemetry embeds seamlessly into the existing JSONB column `pos.terminals.device_health` (established in migration 021).
- Offline-first operational integrity: offline terminals continue operations under their local SQLite lease, and update checks fail closed or defer until valid network connectivity and verified manifests are available.

## Implemented Scope (Exact)

### 1. Stable & Beta Update Channels
- Supported update channels formalized: `stable`, `beta`, and `dev`.
- Production channels designated: `stable` and `beta`.
- Cross-channel mismatch protection: manifest channel must strictly match expected channel during validation and consumption.
- Package file name parity enforcement: packages with `-stable-` or `-beta-` markers must match the manifest channel, preventing channel cross-contamination.
- PosServer channel isolation: `CheckForUpdateAsync` filters strictly on `r.channel = @channel`, ensuring beta releases are never served to stable terminals and vice versa.

### 2. Signed Artifacts Policy Enforcement
- Extended `UpdatePackageManifest` domain record with `IsSigned` (bool) and `SigningThumbprint` (string?).
- Authenticode signature policy: Production channels (`stable` and `beta`) strictly require `IsSigned == true` and a non-empty `SigningThumbprint` when production policy is enforced.
- Development channel (`dev`) allows unsigned packages for developer velocity and local staging.
- Bad hashes, corrupted package sizes, or missing thumbprints on signed packages fail validation immediately.

### 3. Staged Rollout & Cohort Targeting
- Verified and locked cohort targeting using PosServer `TargetTerminalIds` against `pos.update_release_targets`.
- Terminals in the targeted cohort receive staged updates when available.
- Terminals outside the cohort are excluded and receive `already_current_no_release_or_outside_cohort` with no release artifact.
- Untargeted releases (general availability) are served to all active terminals for the designated channel.
- Audit logging tracks cohort targeting (`updates.release.cohort.targeted`) upon release reconciliation.

### 4. Rollback Package Integrity Verification
- Created `RollbackPackageService` and `RollbackPackageInfo` to deterministically validate rollback baselines.
- Verifies package existence, size in bytes, and SHA-256 hexadecimal checksum against the manifest.
- Validates version distinction: `RollbackVersion` cannot be identical to `TargetReleaseVersion`.
- Enforces channel consistency between the rollback package and the deployment channel.
- Enforces Authenticode signing for rollback artifacts targeting production channels.
- Enforces strict architectural guardrails: `SchemaVersion == 4` and `SyncContract == "schema_version_4"`.

### 5. Update Health Telemetry & Sanitized Evidence
- Created `UpdateHealthTelemetry` with standardized lifecycle states:
  - `checked`: Terminal evaluated update availability.
  - `downloaded`: Package downloaded and checksum verified.
  - `applied`: Update successfully installed and launched.
  - `failed`: Update installation failed (requires error message).
  - `rollback_triggered`: Rollback initiated due to health check or startup fault (requires rollback version).
  - `rollback_completed`: Rollback successfully applied and verified (requires rollback version).
- Integrated `UpdateHealthEvidenceDto` into `TerminalDeviceHealthDto` in `PosServer.Contracts`, enabling terminals to report update health via existing heartbeat payloads (`POST /api/v1/terminal/heartbeat`).
- Built `UpdateHealthSanitizer` to scrub sensitive data before persistence or transmission:
  - Connection strings (passwords, user IDs, hosts).
  - Bearer tokens and JWT strings.
  - Key-value secrets, API keys, and authorization tokens.
  - Credit card PANs (13–19 digits).

### 6. PosBuilder & Tooling Hardening
- Extended PosBuilder CLI (`create-update-package`, `validate-update-package`) to support `--signed`, `--signing-thumbprint`, `--rollback-version`, `--rollback-package-hash`, `--channel`, and `--require-signature`.
- Updated `PosBuilderSelfTestRunner` to support channel and signature generation for automated test flows.

## Database Impact
- `DATABASE_MIGRATION_REQUIRED: NO`
- No PostgreSQL migration files created or modified.
- `pos.terminals.device_health` JSONB column stores update health evidence natively without schema mutation.
