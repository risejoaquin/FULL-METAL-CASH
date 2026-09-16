# Validation Commands: SolidPOS V1.1-11 PosDashboard Operations Center

## 1. Secret Scanning
```powershell
pwsh -File ./scripts/security/scan-local-secrets.ps1 -Root .
```

## 2. PosDashboard Local Verification
```powershell
Push-Location src/PosDashboard/SolidPOS.PosDashboard.Admin
npm ci
npm run self-test
npm run build
Pop-Location
```

## 3. V1.1-11 Static Contract Validation
```powershell
pwsh -File ./scripts/v1.1/validate-v1.1-11-posdashboard-operations-center.ps1 -StaticOnly
```

## 4. V1.1-11 Full Validation
```powershell
pwsh -File ./scripts/v1.1/validate-v1.1-11-posdashboard-operations-center.ps1
```

## 5. Previous V1.1 Phase Validators
```powershell
pwsh -File ./scripts/v1.1/validate-v1.1-02-production-metrics-alerting.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-03-advanced-health-readiness.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-04-postgresql-query-hardening.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-05-poscore-error-ux-recovery.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-06-sync-self-healing.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-07-offline-diagnostics-support-bundle.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-08-terminal-device-management-hardening.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-09-update-channel-velopack-hardening.ps1 -StaticOnly
pwsh -File ./scripts/v1.1/validate-v1.1-10-crash-reporting-safe-telemetry.ps1 -StaticOnly
```

## 6. .NET Solution Build and Tests
```powershell
dotnet build solidpos-platform.sln --configuration Release
dotnet test solidpos-platform.sln --configuration Release
```

## 7. Git Diff Cleanliness Check
```powershell
git diff --check
```
