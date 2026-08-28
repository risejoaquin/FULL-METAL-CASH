# SolidPOS EXP-10 HOTFIX 10.3 — SQL psql UUID Variable Quoting Contract

## Estado

PENDING USER VALIDATION.

## Motivo

EXP-10 HOTFIX 10.2 avanzó correctamente hasta el SQL cross-check, pero PostgreSQL rechazó el cast de `tenant_id` porque el archivo SQL re-declaraba variables de psql con `\set tenant_id :'tenant_id'` y después volvía a leerlas como `:'tenant_id'::uuid`.

Eso produjo un valor con comillas anidadas:

```text
''uuid''::uuid
```

El error observado fue:

```text
invalid input syntax for type uuid: "'0ce5bbd0-528b-4aee-9fe3-93df001a4fde'"
```

## Corrección

Se eliminó el bloque de re-declaración `\set` del SQL de EXP-10.

El SQL ahora consume directamente las variables recibidas por `psql -v` desde el validador PowerShell:

```sql
:'tenant_id'::uuid
:'customer_id'::uuid
:'user_id'::uuid
:'store_id'::uuid
:'role_code'::text
```

## Archivos modificados

```text
scripts/expansion/exp-10-customer-admin-management-completion-check.sql
SOLIDPOS_EXP_10_HOTFIX_10_3.md
SOLIDPOS_EXP_10_CUSTOMER_ADMIN_MANAGEMENT_COMPLETION.md
EXP_10_VALIDATION_COMMANDS.md
```

## Alcance

No modifica backend, migraciones, ventas, pagos, caja, inventario, stores, terminals, sync, release channels ni tenant identity.

## Resultado esperado

```text
[EXP-10] SQL customer/admin management cross-check PASS
[EXP-10] EXP-10 PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11
```
