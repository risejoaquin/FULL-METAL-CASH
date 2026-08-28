# SolidPOS Iteration 19 — Validation Commands

## Backend / solution validation

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln
```

```powershell
dotnet test solidpos-platform.sln
```

## PosDashboard validation

```powershell
.\scripts\posdashboard\validate-posdashboard-admin-react.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

## Expected result

```text
PosDashboard admin React self-test started.
Vite React project ready.
Tailwind foundation ready.
Login view ready: /api/v1/auth/login
Admin layout ready: Overview, Sales, Sync, Tenants, Security, Settings
Dashboard client ready: /health/ready and /api/v1/sync/status
PosDashboard admin React foundation completed.
```
