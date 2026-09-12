# V1.1-06 Validation Commands

Run from the repository root.

```powershell
.\scripts\security\scan-local-secrets.ps1 -Root .
.\scripts\v1.1\validate-v1.1-06-sync-self-healing.ps1 -StaticOnly
dotnet restore .\solidpos-platform.sln
dotnet build .\solidpos-platform.sln --configuration Release --no-restore
dotnet test .\tests\SolidPOS.PosCore.UnitTests\SolidPOS.PosCore.UnitTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~SyncSelfHealingTests|FullyQualifiedName~RemoteSyncPushServiceTests|FullyQualifiedName~LocalOutboxBatchPlannerTests"
dotnet test .\solidpos-platform.sln --configuration Release --no-build
git diff --check
```
