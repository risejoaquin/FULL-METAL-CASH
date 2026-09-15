# SolidPOS V1.1-09: Validation Commands

Run the following commands in sequence from the repository root (`C:\Users\Lucilfer\Documents\SolidPos`):

### 1. Secret Scanner
```powershell
./scripts/security/scan-local-secrets.ps1 -Root .
```
Expected result: `No obvious secret patterns found.`

### 2. Static Phase Validator
```powershell
./scripts/v1.1/validate-v1.1-09-update-channel-velopack-hardening.ps1 -StaticOnly
```
Expected result: `PASS V1.1-09 UPDATE CHANNEL & VELOPACK HARDENING STATIC CONTRACT`

### 3. Restore Solution
```powershell
dotnet restore solidpos-platform.sln
```
Expected result: Restore completed successfully with 0 errors.

### 4. Build Solution (Release)
```powershell
dotnet build solidpos-platform.sln --configuration Release --no-restore
```
Expected result: 0 errors, 0 warnings.

### 5. Focused Update Channel Hardening Tests (PosCore)
```powershell
dotnet test tests/SolidPOS.PosCore.UnitTests --filter "FullyQualifiedName~UpdateChannelHardeningTests" --configuration Release --no-build
```
Expected result: All 18 update channel, signing policy, rollback package, and health sanitization tests pass.

### 6. Focused Staged Rollout & Cohort Tests (PosServer)
```powershell
dotnet test tests/SolidPOS.PosServer.UnitTests --filter "FullyQualifiedName~StagedRolloutChannelTests" --configuration Release --no-build
```
Expected result: All 4 staged rollout, channel isolation, and telemetry serialization tests pass.

### 7. Full Solution Test Suite
```powershell
dotnet test solidpos-platform.sln --configuration Release --no-build
```
Expected result: 226 tests pass across all 4 test projects with 0 failures.

### 8. Full Phase Validator
```powershell
./scripts/v1.1/validate-v1.1-09-update-channel-velopack-hardening.ps1
```
Expected result: `PASS V1.1-09 UPDATE CHANNEL & VELOPACK HARDENING FULL VALIDATION`

### 9. Git Diff Check
```powershell
git diff --check
```
Expected result: Clean with 0 whitespace errors.
