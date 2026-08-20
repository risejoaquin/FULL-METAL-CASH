# SolidPOS Iteration 19 — PosDashboard Admin React Foundation

## Status

Prepared for local validation.

## Scope

This iteration introduces the first real PosDashboard admin application using Vite, React, TypeScript and Tailwind.

## Implemented

- `src/PosDashboard/SolidPOS.PosDashboard.Admin`
- Vite React project foundation.
- Tailwind base styling.
- Login form targeting `/api/v1/auth/login`.
- Session persistence through browser localStorage.
- Admin shell layout.
- Navigation placeholders: Overview, Sales, Sync, Tenants, Security, Settings.
- Operational overview cards.
- PosServer client for `/health/ready` and `/api/v1/sync/status`.
- Dashboard self-test.
- PowerShell validation script.

## Architectural rule

```text
React Views
→ Feature Components
→ PosServer API Client
→ PosServer HTTP Contracts
```

The dashboard does not duplicate POS runtime logic. It only consumes the backend contract.

## Commands

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
.\scripts\posdashboard\validate-posdashboard-admin-react.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```
