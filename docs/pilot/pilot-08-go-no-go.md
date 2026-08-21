# PILOT-08 GO / NO-GO

## GO

- Backup schema-only generado.
- SHA-256 de backup generado.
- Restore local aislado validado.
- Rollback productivo validado con cero filas persistidas.
- Produccion queda healthy despues del drill.

## NO-GO

- `pg_dump` falla.
- Restore local falla.
- Falta una tabla critica restaurada.
- Rollback deja filas persistidas.
- Produccion no queda ready despues del drill.
