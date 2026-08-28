# Macro Fase 32 Hotfix 32.1 — PostgreSQL Trigger Compatibility

## Estado
IMPLEMENTED — pending local validation

## Problema
La migración `014_builder_updates_runtime.sql` fallaba en PostgreSQL con:

```text
ERROR: syntax error at or near "NOT"
LINE 1: CREATE TRIGGER IF NOT EXISTS trg_builder_projects_updated_at
```

## Causa
PostgreSQL no acepta `CREATE TRIGGER IF NOT EXISTS` en el formato usado por la migración.

## Corrección
Se reemplazó por un bloque idempotente:

```sql
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_trigger
    WHERE tgname = 'trg_builder_projects_updated_at'
      AND tgrelid = 'pos.builder_projects'::regclass
  ) THEN
    CREATE TRIGGER trg_builder_projects_updated_at
    BEFORE UPDATE ON pos.builder_projects
    FOR EACH ROW EXECUTE FUNCTION pos.touch_updated_at();
  END IF;
END $$;
```

## Alcance
Solo cambia la migración 014.

No cambia:
- endpoints
- OpenAPI
- servicios
- contratos
- permisos funcionales
- reglas de negocio
