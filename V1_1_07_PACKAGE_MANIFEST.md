# V1.1-07 Package Manifest

Phase: V1.1-07 Offline Diagnostics & Support Bundle

Baseline: 84317853c5b5d1533358ddc897e7f560aea470f1

Scope:

- schemaVersion: 4
- syncContract: schema_version_4
- Add PosCore offline diagnostic bundle models and service.
- Add support bundle manifest, runtime, sqlite, sync, hardware, and sanitized log export.
- Add defensive sanitizer for secrets, connection strings, JWTs, bearer tokens, private keys, and PII.
- Add export-support-bundle command to PosCore CLI.
- Add focused SupportBundleService unit tests.
- Add V1.1-07 static validator and CI step.

Modified/created files:

- SOLIDPOS_V1_1_07_OFFLINE_DIAGNOSTICS_SUPPORT_BUNDLE.md
- V1_1_07_PACKAGE_MANIFEST.md
- V1_1_07_VALIDATION_COMMANDS.md
- scripts/v1.1/validate-v1.1-07-offline-diagnostics-support-bundle.ps1
- .github/workflows/solidpos-ci.yml
- src/PosCore/SolidPOS.PosCore.Application/Diagnostics/SupportBundleModels.cs
- src/PosCore/SolidPOS.PosCore.Application/Diagnostics/SupportBundleService.cs
- src/PosCore/SolidPOS.PosCore.Cli/Program.cs
- tests/SolidPOS.PosCore.UnitTests/SupportBundleServiceTests.cs

No production database migration is included.
No PosServer modifications are included.
