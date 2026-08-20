# SolidPOS Iteration 19 Hotfix 19.1 — PosDashboard TypeScript Module Resolution

## Status
Prepared. Requires user validation.

## Problem
The dashboard self-test passed, but `npm run build` failed under the installed TypeScript version:

```text
tsconfig.json:13:25 - error TS5108: Option 'moduleResolution=node10' has been removed.
```

The validation script continued after the failed build, so Iteration 19 could not be closed as PASS.

## Fix
- Changed dashboard `tsconfig.json` from `moduleResolution: "Node"` to `moduleResolution: "Bundler"`.
- Hardened `scripts/posdashboard/validate-posdashboard-admin-react.ps1` so every Node/npm command checks `$LASTEXITCODE` and fails immediately if restore/build/self-test fails.

## Files changed
- `src/PosDashboard/SolidPOS.PosDashboard.Admin/tsconfig.json`
- `scripts/posdashboard/validate-posdashboard-admin-react.ps1`

## Validation
```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```

```powershell
.\scripts\posdashboard\validate-posdashboard-admin-react.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

Expected:

```text
Building dashboard...
vite build
built in ...
Running dashboard self-test...
PosDashboard admin React foundation completed.
message : PosDashboard admin React foundation completed.
```
