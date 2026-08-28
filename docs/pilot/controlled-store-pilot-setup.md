# SolidPOS PILOT-01 — Controlled Store Pilot Setup

## Objective

Prepare the production tenant for a controlled store pilot without adding new product features. This phase verifies that the production environment is ready to run real controlled operations with a known tenant, store, admin user, initial product, cash payment method, active terminal, dashboard, audit trail, and rollback documentation.

## Scope

PILOT-01 validates the operational setup required before real sales:

- Production liveness and readiness.
- PostgreSQL runtime table readiness.
- Admin login for the pilot tenant.
- Protected metrics endpoint.
- Sync runtime status.
- Sales read model availability.
- Audit events read model availability.
- Dashboard production build and self-test.
- Tenant/store/admin/product/payment/terminal setup via PostgreSQL.
- Daily pilot log initialization.
- Opening/closing checklists.
- Rollback plan.

## Production baseline

| Item | Value |
| --- | --- |
| Tenant ID | `0ce5bbd0-528b-4aee-9fe3-93df001a4fde` |
| Store code | `MAIN` |
| Admin email | `admin@micafeteria.com` |
| Initial product SKU | `QSR-AMERICANO` |
| Payment method | `cash` |
| Expected sync schema | `4` |

## GO criteria

PILOT-01 is GO only when all of these are true:

- `/health/live` returns `alive`.
- `/health/ready` returns `ready` and `database = ready`.
- Admin login returns a valid access token.
- Protected metrics show `database.ready = true` and `requiredTablesPresent = true`.
- Dashboard production build passes.
- Dashboard self-test passes.
- Sales read model returns a valid response.
- Audit events read model returns a valid response.
- Tenant is active.
- Store is active.
- Admin user is active and unlocked.
- Admin user has access to the pilot store.
- Product `QSR-AMERICANO` is active and sellable.
- Product has a positive price.
- Cash payment method is active.
- At least one active terminal exists for the pilot store.
- Daily pilot log is present.

## NO-GO criteria

Stop the pilot if any of these happen:

- Production readiness fails.
- Admin login fails.
- Database readiness fails.
- Dashboard production build fails.
- Local secret scan finds obvious secrets.
- Product, cash payment method, store, or terminal is missing.
- Audit events endpoint fails.
- Sync status endpoint fails.
- Refresh-token rotation from Iteration 21 is not confirmed.

## Validation command

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$securePassword = Read-Host -AsSecureString "Production admin password"

.\scripts\pilot\validate-controlled-store-pilot-setup.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```

## Expected result

```text
[PILOT-01] Local repository guardrails PASS
[PILOT-01] Local secret scan PASS
[PILOT-01] PosDashboard production build and self-test PASS
[PILOT-01] Production liveness PASS
[PILOT-01] Production readiness PASS
[PILOT-01] Admin login PASS
[PILOT-01] Protected metrics PASS
[PILOT-01] Sync runtime status PASS
[PILOT-01] Sales read model availability PASS
[PILOT-01] Audit events read model availability PASS
[PILOT-01] Controlled store data setup via PostgreSQL PASS
[PILOT-01] Pilot daily log initialized PASS

message : SolidPOS PILOT-01 controlled store pilot setup completed.
goNoGo  : GO
```
