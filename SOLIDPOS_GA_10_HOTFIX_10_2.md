# SolidPOS GA-10 Hotfix 10.2 — DB JSON Output Parser Guardrail

## Estado

Hotfix de validator/script. No modifica backend, dashboard, migraciones ni contratos C#.

## Causa

GA-10.1 llegó correctamente hasta `Database observability/readiness snapshot`, pero el parser PowerShell no pudo extraer JSON desde la salida de `psql`/Docker.

## Corrección

- Versión del validator: `GA-10.2-db-json-output-parser-guardrail`.
- `psql` ahora ejecuta con `-X -q -tA -P footer=off`.
- El SQL emite un marcador estable `GA10_JSON:` antes del payload.
- `Invoke-DbJsonFile` ahora soporta:
  - línea con marcador;
  - línea JSON cruda;
  - salida multilinea con extracción entre `{` y `}`;
  - muestra de salida si no puede parsear.

## Decisión técnica

La falla fue del validator, no de PosServer ni de Railway. Los checks previos ya habían pasado: repository guardrails, build, tests, secret scan y observability endpoint/auth.

## No se modificó

- `src/`
- `tests/`
- `database/migrations/`
- contratos C#
- `schemaVersion`
- `syncContract`

