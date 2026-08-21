# SolidPOS PILOT-05 HOTFIX 05.4 - Recipe SQL contract lookup

Status: PENDING USER VALIDATION
Date: 2026-08-20

## FAIL received

PILOT-05 reached the real production contract lookup step and failed with:

```text
ERROR: column p.recipe_id does not exist
```

This is a validator bug, not a backend/offline runtime failure.

## Root cause

The PILOT-05 validator assumed the recipe relation was stored on `pos.products.recipe_id`.
The real PostgreSQL schema stores product-to-recipe relation through:

```text
pos.recipes.output_product_id -> pos.products.id
pos.recipe_items.recipe_id -> pos.recipes.id
```

## Change

Updated:

```text
scripts/pilot/validate-offline-mode-field-test.ps1
```

The recipe item lookup now joins the real schema:

```sql
pos.recipe_items ri
JOIN pos.recipes r ON r.tenant_id = ri.tenant_id AND r.id = ri.recipe_id
JOIN pos.products p ON p.tenant_id = r.tenant_id AND p.id = r.output_product_id
```

## Modules affected

```text
scripts/pilot
```

## Decision

Keep PILOT-05 scoped to the actual production DB contract and avoid schema assumptions that are not present in the current PostgreSQL schema.

## Risk

This hotfix only fixes the preflight recipe SQL lookup. PILOT-05 may still surface a later real offline/sync/cash/read-model issue once this gate passes.

## Validation performed here

No `dotnet build` or `dotnet test` was executed in this environment.

Static checks performed:

```text
validate-offline-mode-field-test.ps1 contains no p.recipe_id reference
validate-offline-mode-field-test.ps1 keeps UTF-8 BOM
validate-offline-mode-field-test.ps1 contains no Markdown fences
validate-offline-mode-field-test.ps1 contains no here-strings
```

## Expected result after applying hotfix

The script should pass:

```text
[PILOT-05] Production offline/sync contract lookup PASS
```

Then continue to terminal enrollment/register and local PosCore offline runtime validation.
