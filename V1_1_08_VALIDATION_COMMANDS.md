# SolidPOS V1.1-08: Validation Commands

Run the following commands in sequence from the repository root (`C:\Users\Lucilfer\Documents\SolidPos`):

### 1. Secret Scanner
```powershell
./scripts/security/scan-local-secrets.ps1 -Root .
```
Expected result: `No obvious secret patterns found.`

### 2. Static Phase Validator
```powershell
./scripts/v1.1/validate-v1.1-08-terminal-device-management-hardening.ps1 -StaticOnly
```
Expected result: `PASS V1.1-08 TERMINAL & DEVICE MANAGEMENT HARDENING STATIC CONTRACT`

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

### 5. Focused Terminal Hardening Tests
```powershell
dotnet test tests/SolidPOS.PosServer.UnitTests --filter "FullyQualifiedName~Terminals" --configuration Release --no-build
```
Expected result: All terminal unit tests pass (including `TerminalValidationMiddlewareTests` and `TerminalEnrollmentServiceTests`).

### 6. OpenAPI Contract Parity Tests
```powershell
dotnet test tests/SolidPOS.PosServer.ContractTests --configuration Release --no-build
```
Expected result: 4 tests pass including route matching and ProblemDetails contracts.

### 7. Full Solution Test Suite
```powershell
dotnet test solidpos-platform.sln --configuration Release --no-build
```
Expected result: 203 tests pass across all 4 test projects with 0 failures.

### 8. Git Diff Check
```powershell
git diff --check
```
Expected result: Clean with 0 whitespace errors.
