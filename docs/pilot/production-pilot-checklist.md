# SolidPOS Production Pilot Checklist

## Backend/API

- [ ] `dotnet restore solidpos-platform.sln` PASS.
- [ ] `dotnet build solidpos-platform.sln` PASS.
- [ ] `dotnet test solidpos-platform.sln` PASS.
- [ ] `/health/live` PASS.
- [ ] `/health/ready` PASS.
- [ ] `/api/v1/observability/metrics` PASS.
- [ ] `/api/v1/sync/contract` PASS con schema 4 o superior.
- [ ] `/api/v1/sync/status` PASS.
- [ ] `/api/v1/provisioning/status` PASS.

## Seguridad

- [ ] Supabase password rotado.
- [ ] Railway redeploy realizado después de cambiar variables.
- [ ] JWT signing key rotado.
- [ ] PROVISION_KEY rotado.
- [ ] Refresh tokens revocados.
- [ ] `active_refresh_tokens = 0` confirmado.
- [ ] Scanner local de secretos PASS.
- [ ] `.gitignore` validado.

## Operación POS

- [ ] Tenant productivo confirmado.
- [ ] Store principal confirmado.
- [ ] Admin productivo confirmado.
- [ ] Producto QSR base disponible.
- [ ] Payment method cash disponible.
- [ ] Flujo venta/caja/recibo validado al menos una vez antes del piloto.

## Dashboard

- [ ] `npm install` PASS.
- [ ] `npm run build` PASS.
- [ ] Self-test dashboard PASS.
- [ ] Overview visible.
- [ ] Reports visible.
- [ ] Operations visible.
- [ ] Audit visible.
- [ ] Cliente usa `/api/v1/audit/events`.

## GO/NO-GO

- [ ] Todos los checks obligatorios están en PASS.
- [ ] Hay plan de rollback.
- [ ] Hay responsable de monitoreo durante piloto.
- [ ] Hay responsable de DB/Supabase.
- [ ] Hay responsable de Railway/deploy.
