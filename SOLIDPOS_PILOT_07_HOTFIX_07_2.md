# SolidPOS PILOT-07 HOTFIX 07.2 — psql Argument Ordering

## Estado

PENDING USER VALIDATION

## Motivo

PILOT-07 HOTFIX 07.1 avanzo correctamente hasta el SQL cross-check, pero el wrapper Docker/psql del validador pasaba `-tAc` antes de las variables `-v`.

En `psql`, `-c` consume el siguiente argumento como comando SQL. Por eso `-v` termino siendo interpretado como SQL y PostgreSQL reporto:

```text
ERROR: syntax error at or near "-"
LINE 1: -v
```

Luego el archivo SQL recibio `:'tenant_id'` sin sustitucion de variable y fallo con:

```text
syntax error at or near ":"
```

## Cambio

Archivo corregido:

```text
scripts/pilot/validate-dashboard-operations-monitoring.ps1
```

Antes:

```powershell
psql $DatabaseUrl -tAc -v tenant_id=... -f /sql/file.sql
```

Ahora:

```powershell
psql $DatabaseUrl -tA -v ON_ERROR_STOP=1 -v tenant_id=... -f /sql/file.sql
```

## Decision tecnica

El validador usa `-f` para ejecutar el archivo SQL y `-v` para pasar variables a psql. No se usa `-c` porque no se esta enviando SQL inline.

## Modulos afectados

```text
scripts/pilot
```

No se toca backend, Dashboard, PosCore, migraciones ni seed productivo.

## Resultado esperado inmediato

```text
[PILOT-07] SQL cross-check against operations metrics PASS
```

## Protocolo

PILOT-07 HOTFIX 07.2 queda PENDING USER VALIDATION hasta que el script termine en PASS REAL PRODUCTION / GO.
