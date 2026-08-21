# PILOT-08 Operator Checklist

- Confirmar que Docker Desktop esta activo.
- Confirmar `DATABASE_URL` de Supabase en la sesion.
- Confirmar password admin en `$securePassword`.
- Ejecutar `validate-backup-restore-rollback-drill.ps1`.
- Verificar que el restore se hace en contenedor aislado.
- Verificar que `persistedRollbackRows = 0`.
- Guardar log y manifest.
