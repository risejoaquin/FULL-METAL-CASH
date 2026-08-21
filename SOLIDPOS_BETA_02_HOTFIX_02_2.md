# SolidPOS — BETA-02 HOTFIX 02.2

## Motivo

La validación real de BETA-02 alcanzó correctamente restore/build/test y autenticación, pero el SQL cerró con:

`foreign_tenant_fixture_incomplete`

El problema estaba en el **contrato del validador**, no en una fuga cross-tenant: el script trataba la ausencia de al menos un recurso extranjero de cada tipo (store/user/terminal/product) como blocker obligatorio. Un tenant extranjero puede existir legítimamente sin tener pobladas todas esas familias.

## Corrección

1. `foreign_tenant_fixture_incomplete` deja de ser blocker.
2. La cobertura de fixtures extranjeros se reporta como `conditions` por familia:
   - `foreign_store_fixture_unavailable`
   - `foreign_user_fixture_unavailable`
   - `foreign_terminal_fixture_unavailable`
   - `foreign_customer_fixture_unavailable`
   - `foreign_product_fixture_unavailable`
3. Sigue siendo blocker no disponer de **ningún tenant extranjero**, porque entonces no existe base de separación inter-tenant en producción.
4. Se endurece el SQL verificando en producción que las tablas núcleo `stores`, `users`, `terminals`, `customers`, `products` y `sales` tengan RLS habilitado y su policy `tenant_isolation_*` presente.
5. Las pruebas API negativas siguen ejecutándose para cada recurso extranjero realmente disponible. Las listas tenant-scoped siguen comparándose contra el source-of-truth SQL del tenant objetivo.

## Alcance

No modifica lógica de negocio, contratos HTTP, esquema de datos ni datos de producción. Solo corrige y endurece el validador BETA-02 y su evidencia.

## Estado

`BETA-02 HOTFIX 02.2 — PENDING USER VALIDATION`
