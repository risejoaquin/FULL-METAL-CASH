# SolidPOS Iteration 20 — Hotfix 20.1

## Scope

Fixes PosDashboard TypeScript build failure in `AdminLayout.tsx` caused by passing disabled navigation labels (`Sales`, `Sync`, `Tenants`, `Security`, `Settings`) into `onSectionChange`, which accepts only `DashboardSection`.

## Change

- Replaced broad nav label union with `NavItem` using optional `section?: DashboardSection`.
- Enabled navigation items carry a typed `section`.
- Disabled/future navigation items remain visible but cannot call `onSectionChange`.
- Active state uses `item.section` instead of raw label.

## Validation

Run:

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln
```

```powershell
dotnet test solidpos-platform.sln
```

```powershell
.\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

Expected final message:

```text
message : PosDashboard reports audit operations dashboard completed.
```
