# Macro Fase 35 — Security / Secrets / Production Auth Hardening

## Objetivo

Cerrar el deployment productivo con hardening de seguridad operativo alrededor de secretos, autenticación, bloqueo de cuentas, JWT, refresh tokens, headers y escaneo preventivo.

## Cambios implementados

### 1. Rotación de secretos expuestos

Se agrega runbook operativo:

- `scripts/security/rotate-production-secrets-checklist.md`

Incluye el orden correcto para rotar:

1. Password de Supabase/PostgreSQL.
2. Variable Railway `DATABASE_URL` o `ConnectionStrings__Postgres`.
3. `Jwt__SigningKey`.
4. Refresh tokens activos.
5. Demo user `owner@solidpos.local`.

### 2. Eliminación de credenciales demo como práctica productiva

Se agrega script explícito, no automático:

- `scripts/security/disable-demo-user.sql`

Este script suspende `owner@solidpos.local`, bloquea la cuenta y revoca sus refresh tokens. Debe ejecutarse únicamente después de crear y validar un administrador real de producción.

### 3. Password policy real

Se agrega política configurable:

```json
"PasswordPolicy": {
  "MinLength": 12,
  "RequireUppercase": true,
  "RequireLowercase": true,
  "RequireDigit": true,
  "RequireNonAlphanumeric": true,
  "BlockCommonPasswords": true
}
```

La política aplica en:

- creación de usuarios admin
- actualización/cambio de contraseña de usuarios admin

Bloquea contraseñas comunes y valores demo como `Admin123!` para nuevas cuentas operativas.

### 4. Login lockout / brute force protection

Se agrega configuración:

```json
"AccountSecurity": {
  "MaxFailedLoginAttempts": 5,
  "LockoutMinutes": 15
}
```

Se agregan columnas runtime:

- `users.login_failed_count`
- `users.login_last_failed_at`
- `users.locked_until`
- `users.password_changed_at`
- `users.password_reset_required`

Después de múltiples intentos fallidos, la cuenta queda bloqueada temporalmente. El login exitoso limpia los contadores.

### 5. JWT challenge ProblemDetails

Los fallos de autenticación/autorización ahora responden como `application/problem+json` con:

- `401 authentication-required`
- `403 forbidden`
- `traceId`
- `instance`

### 6. Refresh token hardening

Se refuerza la rotación:

- `RotateRefreshTokenAsync` ahora devuelve `bool`.
- Si el token viejo ya fue usado/revocado, la rotación falla.
- Se agregan campos runtime:
  - `refresh_tokens.rotated_at`
  - `refresh_tokens.last_used_at`
  - `refresh_tokens.revoked_reason`
- Logout marca `revoked_reason = 'logout'`.
- Rotación marca `revoked_reason = 'rotated'`.

### 7. Security headers

Se agrega middleware:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy: camera=(), microphone=(), geolocation=()`
- `Cross-Origin-Opener-Policy: same-origin`
- `Cross-Origin-Resource-Policy: same-site`
- `Strict-Transport-Security` cuando la request es HTTPS

### 8. Dependabot / vulnerability scanning

Se agrega:

- `.github/dependabot.yml`
- `.github/workflows/security-scan.yml`

El workflow de seguridad corre manualmente y semanalmente. Revisa paquetes NuGet vulnerables y patrones obvios de secretos.

### 9. Secret scanning local

Se agrega:

- `scripts/security/scan-local-secrets.ps1`

Uso:

```powershell
.\scripts\security\scan-local-secrets.ps1
```

## Migración agregada

```text
015_security_auth_hardening.sql
```

## Validación requerida

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Después:

```powershell
.\scripts\apply-postgresql-migrations.ps1

.\scripts\smoke-test-deployment.ps1 -BaseUrl "https://full-metal-cash-production.up.railway.app"
```

## Decisión arquitectónica

La Fase 35 no elimina el seed demo porque sigue siendo útil para CI, staging y smoke tests. La decisión correcta es separar claramente:

- seed demo: permitido solo en dev/staging/smoke
- producción real: crear admin real, rotar secretos, suspender demo user, revocar refresh tokens

