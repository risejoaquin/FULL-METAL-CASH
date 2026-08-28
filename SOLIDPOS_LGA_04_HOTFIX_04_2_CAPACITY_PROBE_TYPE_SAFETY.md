# SolidPOS LGA-04 HOTFIX 04.2 — Capacity Probe Type Safety

## Problema
El validador LGA-04 fallaba durante `Capacity boundary probe` con `Los tipos de argumentos no coinciden`.

## Causa
La función de concurrencia usaba una lista genérica y agregaba resultados de jobs de PowerShell sin normalizar completamente tipos/arrays. En ciertas versiones de Windows PowerShell, `Receive-Job` puede devolver estructuras que disparan errores de overload/tipos al agregarlas o al agruparlas.

## Cambio
- Versión del validator: `LGA-04.2-capacity-probe-type-safety`.
- Reescritura de `Invoke-ConcurrencyProbe` con arrays PowerShell simples.
- Normalización explícita de `status`, `ms`, `p95Ms`, `successCount`, `failureCount` y `statuses`.
- No cambia la lógica de negocio ni activa Public GA.

## Resultado esperado
LGA-04 debe superar `Capacity boundary probe` y continuar a snapshot DB y blocker matrix.
