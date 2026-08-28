# SolidPOS Production Pilot GO/NO-GO

## GO obligatorio

El piloto puede iniciar solamente si todos estos puntos están en PASS:

- Railway responde `/health/live` con `alive`.
- Railway responde `/health/ready` con `status=ready` y `database=ready`.
- Supabase/PostgreSQL usa la contraseña nueva y Railway apunta al connection string nuevo.
- `active_refresh_tokens = 0` después de la rotación de secretos.
- Login admin productivo funciona con `admin@micafeteria.com`.
- `/api/v1/observability/metrics` confirma `database.ready=True` y `requiredTablesPresent=True`.
- `/api/v1/sync/contract` confirma `currentSchemaVersion >= 4`.
- `/api/v1/sync/status` responde sin error.
- `/api/v1/sales?limit=10` responde sin error.
- `/api/v1/returns?limit=10` responde sin error.
- `/api/v1/audit/events?limit=10` responde sin error.
- PosDashboard compila en producción y ejecuta self-test.
- Scanner local de secretos no encuentra patrones obvios.
- `.gitignore` protege `.env`, `.runtime`, SQLite local, logs, paquetes, `node_modules` y `dist`.

## NO-GO inmediato

No iniciar piloto si ocurre cualquiera de estos puntos:

- `/health/ready` devuelve 503 después de redeploy.
- Login admin devuelve 401 con la contraseña productiva esperada.
- Railway usa un connection string viejo después de rotar Supabase.
- `active_refresh_tokens` es mayor que 0 después del cierre de seguridad.
- El dashboard no compila con `npm run build`.
- El scanner local detecta secretos en archivos versionables.
- Sync contract baja de schema 4.
- Métricas protegidas no pueden validar tablas requeridas.

## Criterio de salida del piloto

El piloto se considera estable si durante la ventana acordada:

- No hay caída de readiness.
- No hay errores de login admin/cajero.
- Las ventas creadas aparecen en reportes.
- Recibos digitales funcionan.
- Sync status no acumula dead letters no resueltas.
- Caja puede abrir, operar y cerrar sin diferencia inesperada.
- Dashboard permite revisar operaciones, reportes y auditoría.
