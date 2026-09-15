# Package Manifest: SolidPOS V1.1-09 Update Channel & Velopack Hardening

- **Phase:** V1.1-09 Update Channel & Velopack Hardening
- **Baseline SHA:** `5ca204fad3c3af658e3e97b1f7efee51bc8dc945`
- **Target Branch:** `lucilferChanges/fastandrun`
- **schemaVersion:** 4
- schemaVersion: 4
- **syncContract:** schema_version_4
- syncContract: schema_version_4
- **Database Migration Required:** NO (Uses existing JSONB storage in `pos.terminals.device_health`)
- **Authoritative Boundary Enforcement:** Channel isolation in `PostgreSqlBuilderUpdatesRepository`, Authenticode signature policy enforcement in `UpdatePackageManifestService` and `RollbackPackageService`.
- **Cohort Targeting:** `pos.update_release_targets` verified via `StagedRolloutChannelTests`.

## Modified and Added Files

1. `src/PosCore/SolidPOS.PosCore.Domain/UpdatePackageManifest.cs`
   - Added `IsSigned`, `SigningThumbprint`, `RollbackVersion`, `RollbackPackageHash` to `UpdatePackageManifest`.
   - Bumped `CurrentManifestVersion` to `"1.1"`.

2. `src/PosCore/SolidPOS.PosCore.Application/Updates/UpdatePackageManifestService.cs`
   - Added `beta` channel to `SupportedChannels`.
   - Defined `ProductionChannels = { "stable", "beta" }`.
   - Added channel mismatch validation and package file name parity checks (`-stable-`, `-beta-`).
   - Implemented Authenticode signature policy enforcement for production channels.
   - Added `ValidateProductionPolicy` method.

3. `src/PosCore/SolidPOS.PosCore.Application/Updates/RollbackPackageService.cs`
   - Created `RollbackPackageInfo`, `RollbackPackageValidationResult`, and `RollbackPackageService`.
   - Validates package existence, size, SHA-256 hash, channel matching, mandatory signing for production channels, and invariants: `SchemaVersion == 4` and `SyncContract == "schema_version_4"`.

4. `src/PosCore/SolidPOS.PosCore.Application/Updates/UpdateHealthTelemetry.cs`
   - Defined update states (`checked`, `downloaded`, `applied`, `failed`, `rollback_triggered`, `rollback_completed`).
   - Created `UpdateHealthEvidence` telemetry record.
   - Created `UpdateHealthSanitizer` to mask connection strings, Bearer tokens, JWTs, credentials, and credit card PANs.

5. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalDeviceHealthDto.cs`
   - Defined `UpdateHealthEvidenceDto` contract.
   - Embedded optional `UpdateHealthEvidenceDto? UpdateHealth = null` in `TerminalDeviceHealthDto`.

6. `src/PosBuilder/SolidPOS.PosBuilder.Wpf/PosBuilderSelfTestRunner.cs`
   - Added support for `--channel`, `--signed`, and `--signing-thumbprint` in automated self-test runs.

7. `src/PosCore/SolidPOS.PosCore.Cli/Program.cs`
   - Extended `create-update-package` CLI command to accept `--signed`, `--signing-thumbprint`, `--rollback-version`, `--rollback-package-hash`.
   - Extended `validate-update-package` CLI command to accept `--channel` and `--require-signature`.

8. `tests/SolidPOS.PosCore.UnitTests/Updates/UpdateChannelHardeningTests.cs`
   - Unit tests for channel validation, cross-channel rejection, signing policies, rollback package validation, invariant guardrails, and telemetry sanitization.

9. `tests/SolidPOS.PosServer.UnitTests/BuilderUpdates/StagedRolloutChannelTests.cs`
   - Unit tests for cohort targeting, non-target exclusion, channel isolation, and telemetry serialization.

10. `.github/workflows/solidpos-ci.yml`
    - Added V1.1-09 static contract validation step.

11. `scripts/v1.1/validate-v1.1-09-update-channel-velopack-hardening.ps1`
    - Phase validation script supporting `-StaticOnly` and full test verification.

12. `SOLIDPOS_V1_1_09_UPDATE_CHANNEL_VELOPACK_HARDENING.md`
    - Technical documentation and scope specification.

13. `V1_1_09_PACKAGE_MANIFEST.md`
    - Phase package manifest.

14. `V1_1_09_VALIDATION_COMMANDS.md`
    - Step-by-step verification commands.
