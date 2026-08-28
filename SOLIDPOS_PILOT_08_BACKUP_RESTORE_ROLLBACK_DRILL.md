# SolidPOS PILOT-08 - Backup / Restore / Rollback Drill

## Objetivo

Validar que SolidPOS puede ejecutar un drill operativo de backup, restore aislado y rollback sin mutar datos productivos de forma persistente.

## Alcance

- Health production.
- Login admin productivo.
- Observability baseline.
- Sync contract schema version 4.
- Snapshot SQL tenant-scoped de origen.
- `pg_dump` schema-only del schema `pos`.
- Restore en PostgreSQL aislado con Docker.
- Validacion de tablas criticas restauradas.
- Rollback transaccional productivo con `ROLLBACK` confirmado.
- Manifest y bitacora piloto.

## No alcance

- No borra datos productivos.
- No ejecuta restore sobre produccion.
- No cambia migraciones.
- No cambia backend, PosCore ni Dashboard.

## Estado

PENDING USER VALIDATION.


## HOTFIX 08.1

Updated Docker PostgreSQL tooling from `postgres:16` to `postgres:17` because production PostgreSQL is 17.6 and `pg_dump` must match or exceed the server major version.


## HOTFIX 08.2

Restore aislado ahora ejecuta bootstrap de extensiones antes de aplicar el dump schema-only:

```sql
CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public;
CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;
```

Motivo: el dump de `pos` puede referenciar `public.citext`; un contenedor PostgreSQL limpio no trae esa extension habilitada por default.


## Hotfixes

- HOTFIX 08.1: PostgreSQL 17 pg_dump/psql tooling.
- HOTFIX 08.2: restore citext/pgcrypto extensions before schema restore.
- HOTFIX 08.3: restore container psql uses explicit TCP host/port and PGPASSWORD via docker exec.
