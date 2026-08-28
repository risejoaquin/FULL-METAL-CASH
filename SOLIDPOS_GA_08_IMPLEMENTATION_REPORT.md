# SolidPOS GA-08 Implementation Report

## Base

Repo validado: GA-07.4 — Single-Result Transactional Rollback Evidence.

## Cambios

### Database

- `database/postgresql/020_ga08_complete_tenant_rls.sql`
- `scripts/apply-postgresql-migrations.ps1` actualizado para aplicar 020.

### Tests

- `PostgreSqlMigrationTests` endurecido para detectar cualquier tabla `pos` con `tenant_id` y RLS deshabilitado.
- nuevo test de write cross-tenant rechazado sobre `background_jobs` con rol limitado.
- `PermissionAuthorizationHandlerTests` agrega caso positivo y negativo de permisos.

### Validation

- `scripts/ga/ga-08-security-tenant-isolation-access-control-check.sql`
- `scripts/ga/ga-08-security-audit-evidence.sql`
- `scripts/ga/validate-ga-08-security-tenant-isolation-access-control-final-gate.ps1`

### Docs

- `SOLIDPOS_GA_08_SECURITY_TENANT_ISOLATION_ACCESS_CONTROL_FINAL_GATE.md`
- `GA_08_VALIDATION_COMMANDS.md`
- `SOLIDPOS_GA_08_IMPLEMENTATION_REPORT.md`

## Decisión técnica

RLS se trata como propiedad estructural de toda tabla tenant-scoped. La auditoría GA-08 cerró diez omisiones históricas, incluidas tablas de refunds, inventory control, provisioning, releases y background jobs. El gate deja de mantener una lista parcial de seis tablas y obtiene cobertura desde el catálogo PostgreSQL.

Para tablas que soportan filas globales (`update_releases`, `background_jobs`), la policy permite `tenant_id IS NULL` y el tenant actual, evitando romper el contrato de releases globales/operaciones del sistema mientras bloquea filas de otro tenant.

## Riesgos

- La migración 020 cambia enforcement de RLS y debe aplicarse antes del validator.
- Si algún repository dependía incorrectamente de leer filas tenant B, GA-08 lo expondrá como defecto real.
- Dependency audit puede quedar `UNAVAILABLE` si NuGet advisory metadata no está disponible; queda como condición, no se inventa PASS de esa subherramienta.
- La credencial productiva expuesta debe rotarse antes del PASS final.

## No cambia

- schemaVersion 4;
- sync contract v4;
- dominio comercial;
- ventas/inventario;
- rollout público;
- activación GA.
