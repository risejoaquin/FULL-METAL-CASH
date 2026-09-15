# SolidPOS V1.1-10: Validation Commands

Run the following commands in sequence from the repository root (`C:\Users\Lucilfer\Documents\SolidPos`):

### 1. Secret Scanner
```powershell
./scripts/security/scan-local-secrets.ps1 -Root .
```
Expected result: `No obvious secret patterns found.`

### 2. Static Phase Validator
```powershell
./scripts/v1.1/validate-v1.1-10-crash-reporting-safe-telemetry.ps1 -StaticOnly
```
Expected result: `PASS V1.1-10 CRASH REPORTING & SAFE TELEMETRY STATIC CONTRACT`

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

### 5. Focused Crash Reporting Tests (PosCore)
```powershell
dotnet test tests/SolidPOS.PosCore.UnitTests --filter "FullyQualifiedName~CrashReportingTests" --configuration Release --no-build
```
Expected result: All 17 crash capture, sanitization, bounded retention, opt-in policy, and correlation tests pass.

### 6. Focused Terminal Device Health Tests (PosServer)
```powershell
dotnet test tests/SolidPOS.PosServer.UnitTests --filter "FullyQualifiedName~TerminalEnrollmentServiceTests" --configuration Release --no-build
```
Expected result: All 13 terminal enrollment and device health crash serialization tests pass.

### 7. Full Solution Test Suite
```powershell
dotnet test solidpos-platform.sln --configuration Release --no-build
```
Expected result: All tests pass across test projects with 0 failures.

### 8. Full Phase Validator
```powershell
./scripts/v1.1/validate-v1.1-10-crash-reporting-safe-telemetry.ps1
```
Expected result: `PASS V1.1-10 CRASH REPORTING & SAFE TELEMETRY FULL VALIDATION`

### 9. Git Diff Check
```powershell
git diff --check
```
Expected result: Clean with 0 whitespace errors.
