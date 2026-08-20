# SolidPOS Iteration 20 Validation Commands

## Backend guardrails

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln
```

```powershell
dotnet test solidpos-platform.sln
```

## PosDashboard operations dashboard validation

```powershell
.\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

## Expected result

```text
Building dashboard production bundle...
vite build
built in ...
Running dashboard operations self-test...
PosDashboard reports/audit/operations self-test started.
Reports client ready: /api/v1/sales and /api/v1/returns.
Operations client ready: /health/ready and /api/v1/sync/status.
Audit client ready: /api/v1/audit.
Admin dashboard sections ready: Overview, Reports, Operations, Audit.
PosDashboard reports audit operations dashboard completed.
message : PosDashboard reports audit operations dashboard completed.
```

## Hotfix 20.1

Fixes PosDashboard TypeScript nav section typing. Repeat full validation after applying this hotfix.
