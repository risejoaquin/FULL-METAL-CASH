# SolidPOS Iteration 12 — PosCore Offline Login / Session / RBAC Local

## Objetivo

Cachear usuarios autorizados, roles/permisos mínimos, permitir login local seguro durante la ventana offline, aplicar permisos locales antes de venta/caja/sync y auditar login/logout/bloqueos.

## Alcance implementado

- Cache local de usuario autorizado en SQLite.
- Hash local PBKDF2-SHA256 para credenciales offline.
- Permisos locales por usuario.
- Sesiones locales con expiración.
- Política de ventana offline máxima de 72 horas.
- Auditoría local de sync de usuario, login, logout y bloqueo por ventana expirada.
- Validación E2E con venta offline bajo sesión local autenticada.

## Tablas SQLite nuevas

- `local_users`
- `local_user_permissions`
- `local_sessions`
- `local_audit_events`

## Comandos nuevos PosCore CLI

- `sync-local-user`
- `login-local`
- `whoami-local`
- `require-permission-local`
- `logout-local`
- `auth-status`

## Criterio PASS

- Build/test limpios.
- Usuario productivo cacheado localmente.
- Login local exitoso.
- Permisos locales aplicados.
- Usuario expirado por más de 72 horas bloqueado.
- Venta offline creada bajo sesión local autorizada.
- Sync push/process remoto exitoso.
- Recibo emitido.
- Cash shift cerrado sin diferencia.
- Dead-letter remoto en 0.
