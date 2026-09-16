# SolidPOS V1.1-12: Validation Commands

## 1. Automated Static Release Closure Gate
```powershell
powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-12-v1-1-release-closure.ps1 -StaticOnly
```

## 2. All V1.1 Phase Static Validators
```powershell
powershell -ExecutionPolicy Bypass -Command "
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-02-production-metrics-alerting.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-03-advanced-health-readiness.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-04-postgresql-query-hardening.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-05-poscore-error-ux-recovery.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-06-sync-self-healing.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-07-offline-diagnostics-support-bundle.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-08-terminal-device-management-hardening.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-09-update-channel-velopack-hardening.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-10-crash-reporting-safe-telemetry.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-11-posdashboard-operations-center.ps1 -StaticOnly
& powershell -ExecutionPolicy Bypass -File scripts/v1.1/validate-v1.1-12-v1-1-release-closure.ps1 -StaticOnly
"
```

## 3. PosDashboard Build Gate (Gate 3)
```powershell
cd src/PosDashboard/SolidPOS.PosDashboard.Admin
npm ci
npm run self-test
npm run build
cd ../../..
```

## 4. Full Build & Test Suite (Gate 1)
```powershell
dotnet build solidpos-platform.sln --configuration Release
dotnet test solidpos-platform.sln --configuration Release --no-build
```

## 5. PosCore WPF Validation (Gate 2)
```powershell
powershell -ExecutionPolicy Bypass -File scripts/poscore/validate-poscore-wpf-sales-flow-qsr.ps1
powershell -ExecutionPolicy Bypass -File scripts/poscore/validate-poscore-wpf-shell.ps1
```

## 6. Security Secret Scan (Gate 7)
```powershell
powershell -ExecutionPolicy Bypass -File scripts/security/scan-local-secrets.ps1 -Root .
```

## 7. Production Health Smoke Test (Gate 4)
```powershell
powershell -ExecutionPolicy Bypass -Command "
Invoke-RestMethod -Uri 'https://full-metal-cash-production.up.railway.app/health/live' | Format-List
Invoke-RestMethod -Uri 'https://full-metal-cash-production.up.railway.app/health/ready' | Format-List
"
```
