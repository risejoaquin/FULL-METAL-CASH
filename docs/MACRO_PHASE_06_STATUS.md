# Macro Phase 06 Status - Catalog Runtime Base

## Goal

Expose a tenant-aware catalog snapshot for POS terminals.

## Implemented

- `GET /api/v1/tenant/catalog`.
- Categories.
- Units.
- Products.
- Variants.
- Barcodes.
- Price lists.
- Active prices.
- Modifier groups.
- Modifiers.
- Product-to-modifier-group mappings.
- Active recipes/BOM.
- Recipe items.
- Tenant-aware PostgreSQL reads using `app.tenant_id`.
- OpenAPI contract update.
- QSR demo catalog seed.
- Unit test for catalog snapshot contract.

## Endpoint Policy

- `GET /api/v1/tenant/catalog`: `catalog.read`

## Demo Catalog Seed

- Latte 12oz.
- Americano 12oz.
- Panini.
- Frappe with 12oz and 16oz variants.
- Cafe molido.
- Leche entera.
- Leche de avena.
- Vaso 12oz.
- Modifier group: Tipo de leche.
- Recipes for Latte and Americano.

## Current Limit

This phase is read-only runtime catalog. Admin CRUD for catalog management will come later.

The next macro phase should implement sales/cash prerequisites or catalog delta sync depending on the chosen route.
