# SolidPOS Iteration 09 Hotfix 09.1 — Production Inventory Recipe Runtime Seed

## Problema

La validación de Iteration 09 compilaba y pasaba tests, pero fallaba en el flujo E2E porque el tenant productivo tenía catálogo (`QSR-AMERICANO`) sin receta/BOM activa.

Resultado observado:

```text
Local inventory cache refreshed. recipes=0; recipeItems=0
SKU QSR-AMERICANO has no local recipe inventory movements.
```

## Causa

Iteration 08 solo necesitaba catálogo/precio. Iteration 09 necesita datos productivos de inventario: receta activa y recipe_items para que PosCore pueda estimar movimientos locales y PosServer genere `inventory_ledger` remoto al procesar `sale.completed`.

## Corrección

Se agregó:

```text
scripts/operations/seed-production-inventory-runtime.sql
```

Y se endureció:

```text
scripts/poscore/validate-poscore-local-inventory-consumption-cache.ps1
```

El script E2E ahora asegura el seed operativo de inventario antes de sincronizar cache local.

## Datos sembrados

```text
QSR-AMERICANO -> recipe active
ING-CAFE-G    -> ingredient
recipe item   -> 18 g
```

## Decisión

No se relaja la validación local. Si no hay receta/BOM, Iteration 09 debe fallar. El hotfix corrige la data operativa productiva mínima, no el contrato.
