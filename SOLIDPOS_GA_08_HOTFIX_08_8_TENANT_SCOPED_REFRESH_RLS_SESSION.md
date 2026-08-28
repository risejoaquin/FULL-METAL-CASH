# SolidPOS GA-08 Hotfix 08.8 — Tenant-scoped refresh RLS session

## Motivo
GA-08.7 confirmó que login y JWT claims pasan, pero el primer refresh falla con `invalid-refresh-token`. La causa arquitectónica es que `pos.refresh_tokens` está protegida por RLS tenant-scoped y el contrato de refresh/logout no transportaba `tenantId`, por lo que la consulta de refresh no podía establecer `app.tenant_id` antes de leer/rotar el token.

## Corrección
- `RefreshTokenRequest` acepta `TenantId` opcional.
- `LogoutRequest` acepta `TenantId` opcional.
- `AuthService.RefreshAsync` pasa `request.TenantId` al repositorio.
- `PostgreSqlAuthRepository` establece `PostgreSqlTenantSession.SetTenantAsync(...)` antes de leer, insertar, rotar o revocar tokens tenant-scoped cuando el tenant está disponible.
- El validator GA-08 envía `tenantId` en refresh, reuse-negative, logout y cleanup.

## Compatibilidad
El campo es opcional para evitar ruptura inmediata del contrato JSON, pero el camino validado para producción GA es tenant-scoped.

## No cambia
- `schemaVersion=4`
- `syncContract=schema_version_4`
- RLS
- JWT access token claims
- password hashing
- refresh token entropy/hash
- CORS/Swagger/headers
