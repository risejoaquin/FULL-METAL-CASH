# V1.1-06 Package Manifest

Phase: V1.1-06 Sync Self-Healing

Baseline: 23beaacb884a92a9a944aa96a2a9d4f88e324c53

Scope:

- schemaVersion: 4
- Add PosCore local sync retry policy.
- Add retry_pending and dead_letter self-healing behavior.
- Add bounded backoff with jitter.
- Add local queue health summary.
- Add focused sync self-healing tests.
- Add V1.1-06 static validator and CI step.

Modified/created files:

- SOLIDPOS_V1_1_06_SYNC_SELF_HEALING.md
- V1_1_06_PACKAGE_MANIFEST.md
- V1_1_06_VALIDATION_COMMANDS.md
- scripts/v1.1/validate-v1.1-06-sync-self-healing.ps1
- .github/workflows/solidpos-ci.yml
- src/PosCore/SolidPOS.PosCore.Domain/OutboxEvent.cs
- src/PosCore/SolidPOS.PosCore.Application/Storage/ILocalPosRepository.cs
- src/PosCore/SolidPOS.PosCore.Application/Sync/LocalSyncRetryPolicy.cs
- src/PosCore/SolidPOS.PosCore.Application/Sync/RemoteSyncPushService.cs
- src/PosCore/SolidPOS.PosCore.Infrastructure/SQLite/SQLiteLocalPosRepository.cs
- tests/SolidPOS.PosCore.UnitTests/SyncSelfHealingTests.cs

No production database migration is included.
