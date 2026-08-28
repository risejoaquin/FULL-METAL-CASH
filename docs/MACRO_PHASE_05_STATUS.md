# Macro Phase 05 Status - Tenant Config + POS Builder Runtime API

## Goal

Expose tenant POS Builder configuration before catalog sync.

## Implemented

- `GET /api/v1/tenant/config`.
- `PUT /api/v1/tenant/config`.
- Tenant-aware PostgreSQL reads using `app.tenant_id`.
- JSONB runtime contract for:
  - `modulesEnabled`
  - `branding`
  - `receiptSettings`
  - `hardwareProfile`
  - `featureFlags`
- QSR MVP dev seed modules:
  - `modifiers`
  - `recipes_bom`
  - `barcode`
  - `cash`
  - `sync`
- Admin update protected by `builder.manage`.
- Terminal/user read protected by `catalog.read`.
- Optimistic version check through `expectedVersion`.
- OpenAPI contract update.
- Unit test for QSR module contract.

## Endpoint Policy

- `GET /api/v1/tenant/config`: `catalog.read`
- `PUT /api/v1/tenant/config`: `builder.manage`

## Current Limit

There is no Dashboard UI yet. Configuration can be updated through the API.

Catalog, prices, modifiers and BOM data are not exposed yet. They begin in the next macro phase.
