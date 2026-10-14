# SolidPOS MVP — Acta de evidencia de aceptación operativa

**Estado de esta plantilla:** sin aprobaciones. Completar por escenario y conservar evidencia junto al SHA exacto del candidato. No subir credenciales, tokens de terminal, datos reales de clientes ni archivos de base de datos con PII.

## Identificación

| Campo | Valor |
| --- | --- |
| Escenario (ID de la matriz v2) | PENDIENTE |
| Requisito MVP | PENDIENTE |
| Commit SHA completo (AC-DEV) | PENDIENTE |
| GitHub Actions run y artifact TRX | PENDIENTE |
| Fecha/hora y zona horaria | PENDIENTE |
| Terminal Windows, versión y periféricos | PENDIENTE |
| Evaluador / rol | PENDIENTE |
| Supervisor / aprobador independiente | PENDIENTE |
| Precondiciones reproducibles | PENDIENTE |

## Pasos, resultados y evidencia

| Paso | Acción y datos descartables | Resultado esperado | Resultado real | Archivo de evidencia |
| --- | --- | --- | --- | --- |
| 1 | PENDIENTE | PENDIENTE | NO EJECUTADO | — |
| 2 | PENDIENTE | PENDIENTE | NO EJECUTADO | — |
| 3 | PENDIENTE | PENDIENTE | NO EJECUTADO | — |

**Resultados permitidos:** PASS / FAIL / BLOCKED / NOT_RUN. No confundir NOT_RUN con PASS.

## Integridad de caja / ventas

- `local_sale_id` y `local_payment_id` deben permanecer estables en reintentos. Anotar identificadores de laboratorio (no secretos).
- Comprobar total, importe recibido, cambio, número de movimientos de caja y outbox mediante inspección **de solo lectura** de la base desechable.
- Una confirmación local NO prueba impresión física ni ACK remoto.
- Si se interrumpe el proceso o falla la impresión, adjuntar estado antes/después, resultado y reproducibilidad.

## Casos manuales críticos de la matriz

| ID | Criterio de aceptación | Tipo |
| --- | --- | --- |
| START-05 | Instalación y arranque en Windows físico | Operativo |
| AUTH-05 | Login/logout/expiración y roles desde WPF | Seguridad |
| CAT-05 | Multiartículo, cantidades, variantes y precio real | UX + persistencia |
| SALE-05 | Cobro físico, cambio y conciliación | Financiero crítico |
| PRN-03 | Impresora física, caída y reintento sin duplicar venta | Periférico |
| SYNC-04 | Venta y ACK verificado con servidor desechable | Integración |
| CASH-04 | Jornada, arqueo, diferencia y cierre autorizados | Financiero crítico |
| REC-06 | Matar proceso antes/después del commit y recuperar | Resiliencia crítico |
| REC-07 | Conciliación supervisada, motivo y auditoría | Seguridad crítico |

## Criterio de cierre

- Resultado por escenario: **NOT_RUN**.
- Defectos Jira/GitHub vinculados: **NINGUNO DOCUMENTADO**.
- Evidencia revisada: **NO**.
- Autorización de Product Owner: **NO**.
- Decisión: **NO-GO**.

Un porcentaje alto de cobertura automatizada NO reemplaza la aceptación humana. El evaluador no podrá declararse independiente cuando él mismo fue autor del cambio sin revisión adicional.
