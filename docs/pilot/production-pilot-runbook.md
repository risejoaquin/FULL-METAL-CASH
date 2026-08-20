# SolidPOS Production Pilot Runbook

## 1. Antes de iniciar

1. Confirmar que la Iteration 21 está cerrada como PASS real production.
2. Confirmar en Supabase:
   ```sql
   SELECT count(*) AS active_refresh_tokens
   FROM pos.refresh_tokens
   WHERE revoked_at IS NULL;
   ```
   Resultado requerido: `0`.
3. Confirmar Railway redeploy exitoso.
4. Confirmar readiness:
   ```powershell
   Invoke-RestMethod -Method Get -Uri "https://full-metal-cash-production.up.railway.app/health/ready"
   ```
5. Ejecutar validación piloto:
   ```powershell
   $securePassword = Read-Host -AsSecureString "Production admin password"

   .\scripts\pilot\validate-production-pilot-readiness.ps1 `
     -BaseUrl "https://full-metal-cash-production.up.railway.app" `
     -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
     -Email "admin@micafeteria.com" `
     -Password $securePassword
   ```

## 2. Durante el piloto

Monitorear:

- `/health/ready`.
- `/api/v1/observability/metrics`.
- `/api/v1/sync/status`.
- Ventas en `/api/v1/sales`.
- Devoluciones en `/api/v1/returns`.
- Auditoría en `/api/v1/audit/events`.
- Dashboard Admin.

## 3. Incidentes y acciones

### Railway readiness 503

1. Revisar último deploy.
2. Revisar `ConnectionStrings__Postgres` o `DATABASE_URL`.
3. Confirmar que la contraseña Supabase corresponde al connection string actual.
4. Redeploy latest.
5. Repetir `/health/ready`.

### Login admin 401

1. Confirmar tenant ID.
2. Confirmar email admin.
3. Confirmar contraseña productiva.
4. Revisar estado de usuario/tenant en Supabase.
5. No rotar JWT otra vez sin confirmar DB y variables.

### Sync con dead letters

1. Revisar `/api/v1/sync/status`.
2. Listar dead letters con `/api/v1/sync/dead-letter`.
3. Reintentar solamente eventos entendidos.
4. No borrar eventos de sync manualmente durante piloto.

### Dashboard no compila

1. Borrar `node_modules` y `dist`.
2. Ejecutar `npm install`.
3. Ejecutar `npm run build`.
4. Si falla, detener avance y corregir antes de piloto.

## 4. Rollback

Rollback permitido:

- Railway: volver al deployment anterior estable.
- Supabase: no revertir migraciones manualmente sin backup/snapshot.
- Secretos: si se rota de nuevo, revocar refresh tokens otra vez.
- Dashboard: si falla el frontend, mantener backend operativo y retirar dashboard del piloto.

## 5. Evidencia requerida

Guardar logs de:

- `dotnet build`.
- `dotnet test`.
- `validate-posdashboard-operations-dashboard.ps1`.
- `validate-production-security-closure.ps1`.
- `validate-production-pilot-readiness.ps1`.
- Resultado SQL `active_refresh_tokens = 0`.
