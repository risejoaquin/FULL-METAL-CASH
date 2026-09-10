# SolidPOS V1.1-03 — Advanced Health / Readiness Diagnostics

**Producto:** SolidPOS
**Bloque:** V1.1-03 — Advanced Health / Readiness Diagnostics
**Baseline:** `main@09ea409f9c94b8fd793df2fd2aced47cfaec4e1a`

## Objetivo

Ampliar `/health/ready` sin crear una nueva superficie pública, conservando su función de gate productivo y agregando diagnóstico seguro y accionable por dependencia.

## Implementación

- breakdown de dependencias: database, schema, sync y storage;
- latencia real del probe PostgreSQL;
- verificación de tablas y columnas críticas del contrato de persistencia;
- compatibilidad explícita con schemaVersion 4;
- contrato sync `schema_version_4` preservado;
- sync readiness basada en estructuras persistentes requeridas, sin exponer datos tenant;
- storage readiness mediante un write/delete probe de almacenamiento temporal;
- estados `ready`, `degraded` y `unavailable`;
- mensajes sanitizados: no se serializan connection strings ni mensajes crudos de excepciones.

## Semántica

- `ready`: todas las dependencias están listas y DB latency <= 1200 ms.
- `degraded`: el servicio sigue disponible, pero DB latency supera 1200 ms o el storage temporal no puede validarse.
- `unavailable`: DB, schema o persistencia requerida por sync no está disponible/compatible. Devuelve HTTP 503.

## Eficiencia

El diagnóstico PostgreSQL usa una sola conexión y un batch de catálogo para tablas/columnas. No consulta filas tenant ni calcula métricas de negocio en readiness. El capacity gate conserva concurrency 3, 6 requests y p95 <= 1200 ms.

## Seguridad

La respuesta puede indicar la fuente lógica de configuración (`ConnectionStrings:Postgres` o `DATABASE_URL`) por compatibilidad histórica, pero nunca devuelve host, usuario, password, URI o connection string. Los errores de driver/configuración se convierten a códigos y textos genéricos.

## PASS

- CI build/test PASS;
- OpenAPI mantiene paridad con runtime;
- schemaVersion 4 y `schema_version_4` preservados;
- `/health/ready` expone breakdown sin secretos;
- capacity gate sin regresión;
- unavailable se diferencia de degraded.
