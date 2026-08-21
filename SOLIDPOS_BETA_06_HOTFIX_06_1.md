# SolidPOS BETA-06 HOTFIX 06.1

## Motivo
El guardrail documental de BETA-06 exigía la frase literal `no destructive`, mientras el runbook ya expresaba correctamente la regla como `must not perform a destructive delete`. Esto produjo un falso negativo antes de ejecutar cualquier operación contra producción.

## Corrección
- Se ajustó el contrato documental para validar el término semántico existente `destructive delete`.
- No se modifica backend, API, SQL de promoción, SQL de rollback, releases ni lógica de producción.
- El rollback continúa siendo transaccional y debe dejar `persistedRollbackMutationCount = 0`.

## Estado
PENDING USER VALIDATION.
