# BETA-05 — Beta Support Operations Drill

## Objetivo
Validar que soporte puede recibir, clasificar, investigar y decidir el siguiente paso ante condiciones reales de la beta usando evidencia reproducible y SLA/runbooks existentes, sin convertir un drill en una mutación destructiva de producción.

## Cobertura
- SEV classification mediante el contrato EXP-08.
- incident intake y evidence package.
- triage de `retry_pending` y `retry_due`.
- triage del dead-letter conocido.
- revisión diaria de open cash shifts.
- métricas protegidas, sync status y audit events.
- decisión explícita de manual retry vs worker.
- decisión explícita de rollback/escalation.
- plantilla de comunicación al cliente.
- resolution checklist.

## Regla operativa
BETA-05 no ejecuta automáticamente retry, eliminación, force-close ni rollback. La fase verifica que exista evidencia suficiente y una ruta de decisión segura. Los dead-letter/retry/open-shift históricos pueden permanecer como condiciones si están triados y no existe blocker nuevo.

## Blockers
- pending conflict sin resolución;
- stale processing > 15 minutos;
- dead-letter sin error/timestamp suficiente para investigar;
- ausencia de audit evidence;
- ausencia de store/terminal activo;
- fallo del contrato EXP-08.

## Resultado esperado
`PASS BETA SUPPORT OPERATIONS DRILL / GO BETA-06`
