# SolidPOS PILOT-08 HOTFIX 08.2 - Restore extension bootstrap

## Estado

PENDING USER VALIDATION

## Motivo

PILOT-08 HOTFIX 08.1 corrigio la version de `pg_dump` a PostgreSQL 17, pero el restore aislado fallo porque el dump schema-only de `pos` referencia el tipo `public.citext` y el contenedor PostgreSQL limpio no tenia instalada la extension `citext`.

Error observado:

```text
ERROR: type "public.citext" does not exist
LINE 5:     email public.citext,
```

## Cambio

Se actualizo:

```text
scripts/pilot/validate-backup-restore-rollback-drill.ps1
```

Antes de ejecutar el restore del dump dentro del contenedor aislado, el script ahora ejecuta:

```sql
CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public;
CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;
```

## Alcance

No se toca produccion de forma destructiva. No se toca backend, PosCore, Dashboard, migraciones ni seeds.

## Resultado esperado

Debe pasar:

```text
[PILOT-08] Restore backup into isolated PostgreSQL container PASS
```

Y continuar con:

```text
[PILOT-08] Restore schema validation...
```
