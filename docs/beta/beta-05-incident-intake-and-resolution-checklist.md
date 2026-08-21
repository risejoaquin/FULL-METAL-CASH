# BETA-05 — Incident Intake and Resolution Checklist

## Incident intake
- [ ] Asignar incident ID.
- [ ] Registrar tenant, store/terminal si aplica y timestamp UTC.
- [ ] Clasificar SEV con la matriz EXP-08.
- [ ] Capturar síntoma, impacto y detector.
- [ ] Adjuntar métricas, audit evidence y SQL evidence sin secretos.

## Triage
- [ ] `retry_pending`: determinar si aún no vence o si requiere worker/manual retry.
- [ ] `retry_due`: revisar error, attempts e idempotencia antes de retry.
- [ ] dead-letter: revisar `error_code`, `error_message`, timestamp y payload metadata; no borrar evidencia.
- [ ] open shift: revisar operador/tienda; no force-close sin evidencia.
- [ ] conflicto: contener y resolver antes de GO.

## Retry / rollback
- [ ] Manual retry solo después de confirmar que la operación es idempotente y que la causa está corregida.
- [ ] Rollback solo con impacto operacional que lo justifique y siguiendo el runbook append-only/no destructive delete.

## Resolution
- [ ] Registrar decisión y owner.
- [ ] Registrar acción ejecutada o razón de no acción.
- [ ] Verificar métricas y SQL después de la acción.
- [ ] Actualizar comunicación al cliente.
- [ ] Cerrar únicamente con evidence package completo.
