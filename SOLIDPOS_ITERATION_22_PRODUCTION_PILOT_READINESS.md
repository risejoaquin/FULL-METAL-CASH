# SolidPOS Iteration 22 — Production Pilot Readiness

## Status

Prepared for validation.

## Goal

Close the system into a production pilot state after security rotation by validating the operational surface required for a real pilot:

- Railway/Supabase readiness.
- Admin login after secret rotation.
- Protected metrics.
- Sync contract/schema 4.
- Sync runtime status.
- Sales read model.
- Returns read model.
- Audit events read model.
- PosDashboard production build.
- Local repository guardrails.
- Pilot GO/NO-GO and incident runbook.

## Changes

### Added

```text
scripts/pilot/validate-production-pilot-readiness.ps1
docs/pilot/production-pilot-runbook.md
docs/pilot/production-pilot-go-no-go.md
docs/pilot/production-pilot-checklist.md
ITERATION_22_VALIDATION_COMMANDS.md
SOLIDPOS_ITERATION_22_PRODUCTION_PILOT_READINESS.md
```

### Modified

```text
src/PosDashboard/SolidPOS.PosDashboard.Admin/src/api/posServerClient.ts
src/PosDashboard/SolidPOS.PosDashboard.Admin/src/features/dashboard/DashboardHome.tsx
src/PosDashboard/SolidPOS.PosDashboard.Admin/src/features/dashboard/AuditDashboard.tsx
src/PosDashboard/SolidPOS.PosDashboard.Admin/scripts/self-test.mjs
scripts/posdashboard/validate-posdashboard-operations-dashboard.ps1
```

## Important correction

The dashboard audit client now targets the actual backend route:

```text
GET /api/v1/audit/events
```

Previous dashboard placeholder text pointed to:

```text
/api/v1/audit
```

That was corrected before pilot readiness because pilot validation must match real server contracts.

## Validation command

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"

.\scripts\pilot\validate-production-pilot-readiness.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword
```

## Expected result

```text
[PILOT] Local repository guardrails PASS
[PILOT] Local secret scan PASS
[PILOT] PosDashboard production build and self-test PASS
[PILOT] Production liveness PASS
[PILOT] Production readiness PASS
[PILOT] Admin login PASS
[PILOT] Protected metrics PASS
[PILOT] Sync contract schema 4 PASS
[PILOT] Sync runtime status PASS
[PILOT] Provisioning status endpoint PASS
[PILOT] Sales read model PASS
[PILOT] Returns read model PASS
[PILOT] Audit events read model PASS
message : SolidPOS production pilot readiness validation completed.
```

## GO/NO-GO rule

Iteration 22 cannot be closed until:

- `active_refresh_tokens = 0` is confirmed in Supabase.
- Production readiness is `ready`.
- Pilot readiness validation script passes.
- Dashboard production build passes.
- Sales, returns and audit read models respond.

## Architectural decision

Iteration 22 does not introduce new database migrations. It hardens the release process and aligns the operational dashboard/client with the real backend contract before a production pilot.
