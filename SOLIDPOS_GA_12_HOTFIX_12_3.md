# SolidPOS GA-12 Hotfix 12.3 — Sync Inbox CreatedAt Schema Compatibility

Fecha: 2026-08-23.

## Diagnóstico

GA-12.2 avanzó hasta el snapshot de base de datos y falló porque el SQL asumía que `pos.sync_inbox_events` tenía columna `updated_at`. En el esquema productivo validado la tabla usa `created_at` para este cálculo.

## Decisión técnica

No se modifica backend, migraciones ni contratos. Se corrige únicamente el SQL del validator GA-12 para calcular `stale_processing_count` usando compatibilidad por JSON (`updated_at` si existe, si no `created_at`) sin referenciar columnas inexistentes.

## Cambios

- `scripts/ga/ga-12-final-general-availability-launch-readiness-check.sql`
- `scripts/ga/validate-ga-12-final-general-availability-launch-readiness.ps1`

Nueva versión del validator: `GA-12.3-sync-inbox-created-at-schema-compatibility`.

## Riesgo

Bajo. El cambio solo afecta lectura de snapshot DB de GA-12. No escribe datos.

## Resultado esperado

`[GA-12] PASS GENERAL AVAILABILITY READINESS / GO CONTROLLED GA ROLLOUT`
