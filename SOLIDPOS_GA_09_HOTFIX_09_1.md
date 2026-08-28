# SolidPOS — GA-09 Hotfix 09.1

Fecha: 2026-08-23.

## Motivo

El validator GA-09.0 falló durante la etapa `Controlled load: health/readiness` por usar `$error` como variable local dentro de PowerShell. PowerShell trata `$error` y `$Error` como el mismo identificador, y `$Error` es una variable reservada/global, por lo que el runtime bloqueó la escritura.

## Tipo de falla

Validator/script. No hay evidencia de defecto en backend, Railway, base de datos, auth, RLS ni dashboard.

## Evidencia previa al fallo

- Repository/source guardrails PASS.
- `dotnet build` PASS.
- `dotnet test` PASS.
- Secret scan PASS.
- GA-08 prerequisite revalidation omitida por switch con evidencia previa.
- Production authentication and baseline protected endpoint checks PASS.
- Database pre-load integrity/capacity snapshot PASS.

## Cambio aplicado

- `scripts/ga/validate-ga-09-performance-capacity-resilience-offline-readiness.ps1`
  - Validator version actualizado a `GA-09.1-powershell-error-variable-type-safety`.
  - Renombradas variables/parámetros PowerShell reservados o ambiguos:
    - `$error` → `$errorMessage`.
    - parámetro `$Error` → `$ErrorMessage`.
  - Se mantiene la propiedad JSON `error` en los objetos de medición para conservar contrato de evidencia.

## Módulos afectados

- Solo scripts GA/documentación GA.
- No se modificó backend.
- No se modificó dashboard.
- No se modificó base de datos.
- No se agregaron migraciones.

## Decisión técnica

Corregir únicamente el validator porque la falla ocurrió antes de completar carga controlada y proviene de semántica de PowerShell, no de comportamiento productivo de SolidPOS.

## Riesgos

- El siguiente intento puede descubrir problemas reales de latencia, resiliencia, DB o endpoints; esos deberán clasificarse con logs nuevos.
- Si falla por thresholds p95/p99, no se debe tocar código automáticamente sin revisar métricas y contexto de Railway.

## Criterio de cierre

GA-09 solo puede cerrar con el log:

```text
[GA-09] GA-09 PASS GA PERFORMANCE CAPACITY RESILIENCE OFFLINE READINESS / GO GA-10
```
