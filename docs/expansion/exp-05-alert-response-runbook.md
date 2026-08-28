# EXP-05 Alert Response Runbook

## Severity

### SEV1

- health/ready no está ready.
- database no está ready.
- pending conflicts > 0 durante operación productiva.
- failed payments last 24h > 0.
- cash shift difference last 24h > 0.

Action: detener expansión, asignar owner, containment, evidence y rollback si aplica.

### SEV2

- retry_pending sync > 0.
- dead_letter sync > 0 conocido.
- negative inventory > 0 conocido.
- failed requests > 0 sin impacto visible.
- p95 latency elevado pero bajo control.

Action: triage, monitoreo, action owner, fecha objetivo.

### SEV3

- documentación incompleta.
- evidence incompleta.
- open cash shift pendiente de cierre operativo.

Action: completar evidence y revisión diaria.

## Triage

1. Capturar timestamp.
2. Capturar endpoint o SQL afectado.
3. Asignar owner.
4. Declarar blocker o condition.
5. Ejecutar containment.
6. Decidir rollback solamente si el impacto afecta venta, caja, pagos, sync o datos.

## Rollback

Rollback no debe improvisarse. Debe usar el runbook de la fase que introdujo el artefacto afectado.
