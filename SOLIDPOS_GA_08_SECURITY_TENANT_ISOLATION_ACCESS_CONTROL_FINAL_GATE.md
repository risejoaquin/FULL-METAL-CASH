# SolidPOS GA-08 — Security, Tenant Isolation and Access Control Final Gate

## Estado

IMPLEMENTED — PENDING REAL PRODUCTION VALIDATION.

GA-08 no se considera PASS hasta obtener exactamente:

```text
[GA-08] GA-08 PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```

## Objetivo

Cerrar la postura de seguridad previa a GA-09 y demostrar que autenticación, autorización, tenant isolation, RLS, store access, provisioning y controles HTTP siguen siendo efectivos contra el candidato real.

## Hallazgo estructural corregido

La auditoría de todas las tablas `pos` con columna `tenant_id` detectó diez tablas que no estaban cubiertas por RLS/policy a lo largo del historial de migraciones:

- `pos.return_refunds`
- `pos.inventory_policies`
- `pos.inventory_low_stock_thresholds`
- `pos.inventory_counts`
- `pos.inventory_count_lines`
- `pos.inventory_transfers`
- `pos.inventory_transfer_lines`
- `pos.production_bootstrap_runs`
- `pos.update_releases`
- `pos.background_jobs`

GA-08 agrega `database/postgresql/020_ga08_complete_tenant_rls.sql`.

La migración:

- habilita RLS en las tres tablas;
- agrega policies idempotentes;
- mantiene el contrato de releases globales (`tenant_id IS NULL`) además de releases tenant-scoped;
- no elimina ni reescribe datos;
- no cambia schemaVersion 4.

## Prevención de regresión

`PostgreSqlMigrationTests` ahora exige que **ninguna tabla base del schema `pos` que tenga `tenant_id` quede sin RLS**.

También se agrega un integration test que usa un rol PostgreSQL limitado y verifica que, con tenant A en `app.tenant_id`, un INSERT de `background_jobs` para tenant B sea rechazado por RLS.

`PermissionAuthorizationHandlerTests` verifica:

- permiso presente => autorización satisfecha;
- permiso ausente => autorización no satisfecha.

## Gate productivo

`validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1` valida:

1. guardrails de source y documentación;
2. secret scan;
3. revalidación fresca de GA-07;
4. RLS y policy coverage para todas las tablas tenant-scoped;
5. security headers;
6. Swagger no expuesto en Production;
7. CORS negativo contra origen no autorizado;
8. login productivo;
9. claims JWT `tenant_id`, `user_id`, `permissions`;
10. refresh rotation;
11. rechazo de reuse del refresh anterior;
12. logout y rechazo del refresh revocado;
13. endpoint protegido sin token => 401;
14. endpoint protegido con admin válido => 200;
15. tenant context correcto;
16. lectura cross-tenant de customer/sale => 404;
17. provisioning status/config/header contract;
18. password/refresh token almacenados como hashes, no plaintext;
19. RBAC, role permissions y store access presentes;
20. dependency vulnerability audit cuando el CLI puede obtenerlo;
21. cleanup de sesiones creadas por el validator;
22. evidencia append-only `ga08.security_final_gate.validated`.

## Credential exposure closure

Durante la validación previa se expuso una cadena de conexión productiva en un log compartido. GA-08 trata esto como blocker operacional.

Antes de ejecutar el validator final:

1. rotar la contraseña/credencial de PostgreSQL usada por producción;
2. actualizar `DATABASE_URL`/connection string del deployment;
3. comprobar `/health/ready`;
4. actualizar `$env:DATABASE_URL` en la terminal local sin imprimirla;
5. ejecutar GA-08 con `-SecretsRotatedAfterExposure`.

El switch no rota secretos. Es una declaración explícita de que la acción operacional ya fue realizada.

## Seguridad de las pruebas

GA-08 no:

- crea tenants;
- crea usuarios productivos de prueba;
- cambia roles comerciales;
- toca ventas/pagos/inventario;
- hace writes cross-tenant contra producción;
- activa GA;
- habilita rollout público.

La prueba negativa de escritura cross-tenant vive en integration tests con PostgreSQL controlado. En producción solo se realizan lecturas negativas y el ciclo normal de auth/refresh/logout.

## Contratos preservados

```text
schemaVersion = 4
syncContract = schema_version_4
generalAvailabilityActivated = false
publicRolloutAllowed = false
```

## Resultado requerido

```text
PASS GA SECURITY TENANT ISOLATION ACCESS CONTROL / GO GA-09
```
