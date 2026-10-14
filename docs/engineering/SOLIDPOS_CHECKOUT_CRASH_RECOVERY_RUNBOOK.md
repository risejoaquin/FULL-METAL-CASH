# SolidPOS — Recuperación de cobro tras interrupción (AC-DEV)

**Estado:** validación técnica; NO-GO comercial. **Ámbito:** terminal Windows, SQLite local, cash-only MVP; sin conexión a proveedores de pago, servidores ni datos productivos.

## Invariantes obligatorias

1. El checkout registra `local_checkout_recovery` con identidad estable ANTES del commit financiero. Un reinicio debe conservar un intento pendiente.
2. Si existe un intento pendiente, el operador **no** debe generar otra venta, abrir/cerrar caja ni descartar su identidad hasta resolverlo.
3. La verificación inspecciona simultáneamente `local_checkout_commits`, `offline_sales`, `local_sale_payments`, `local_cash_movements`, `local_outbox_events` y `local_print_jobs`, sus importes, tienda y terminal. Ningún marcador aislado constituye evidencia suficiente.
4. Cuando la evidencia es íntegra, se marca el intento como confirmado. Esto NO reenvía pago, NO imprime, NO marca outbox como ACK, NO crea otro movimiento de caja.
5. Si falta evidencia o está inconsistente, el estado permanece PENDING. No efectuar modificación manual, eliminación SQL ni cambio directo de estado. Escalar a supervisor para revisión forense y diseño de resolución auditada.
6. `schema_version=4` y contrato outbox `sale.completed` permanecen intactos.

## Procedimiento operador

- Detener nuevos cobros si aparece «resultado incierto».
- Consultar identificador de venta y registrar hora/terminal. Mantener el SQLite y su WAL intactos; no copiar únicamente el archivo .sqlite mientras está abierto.
- Tras autenticarse, utilizar **Verificar cobro pendiente en SQLite**. La acción consulta evidencia, nunca repite la operación.
- Si se confirma, verificar ticket pendiente de impresión y estado del outbox separadamente. No declarar sincronización sin ACK.
- Si persiste bloqueo, entregar a soporte la ID de venta y diagnóstico, evitando exponer tokens o contraseñas. No «solucionar» borrando tablas.

## Matriz de aceptación (pendientes hasta CI)

| Escenario | Resultado exigido |
| --- | --- |
| Reinicio con intención registrada, sin commit | Bloqueo; nueva identidad rechazada |
| Reinicio después del commit | Conciliación del mismo ID sin duplicación |
| Falta movimiento de caja | Bloqueo (fail closed) |
| Falta pago, outbox o trabajo de impresión | Bloqueo (fail closed) |
| Reuso del ID con importe alterado | Rechazo |
| Múltiples intenciones pendientes | Mantener bloqueo y requerir revisión supervisada |
| Desconexión de impresora / red | No convertir estado en pago fallido ni en ACK remoto |

## Iteración de endurecimiento — 2026-10-13

- El shell conserva bloqueo si una segunda intención persiste tras reconciliar la primera.
- Nunca informa éxito cuando falta la evidencia que verifica el journal después del commit.
- Nueva regresión para múltiples intenciones pendientes, incluyendo estado SQLite anómalo.
- Los tres grupos de GitHub Actions deben estar aprobados para el SHA final antes de contar estas correcciones como validadas.

## Riesgos abiertos

- Resolución supervisada con RBAC, motivos, evidencias y auditoría inmutable: **no implementada**.
- Simulación de terminación abrupta del proceso Windows real y validación hardware: **pendiente**.
- Pruebas de recuperación deben contar por separado cobertura CI y aceptación operativa. El CI verde no equivale a GO comercial.
