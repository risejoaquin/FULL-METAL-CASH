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

## Resolución excepcional con supervisor (rama AC-DEV)

La interfaz WPF presenta la opción **Resolver intento SIN venta (auditado)**.
No cancela ni devuelve una venta completada: únicamente permite resolver
un intento previo que **nunca** generó efectos financieros y que fue comprobado
documentalmente.

**Requisitos exigidos por SQLite bajo la misma transacción:**

1. Segundo operador diferente del cajero original, con sesión vigente y mismo tenant/store.
2. Permisos locales `sales.void` y `cash.close` simultáneamente; no se conceden por este flujo.
3. Terminal con binding v4 vigente, motivo detallado, referencia de incidencia/evidencia y **confirmación explícita de inspección de efectivo físico**. Sin esta atestación el servicio deniega la resolución.
4. Ausencia de venta, pago, línea, movimiento de inventario/caja, trabajo de impresión,
   outbox o auditoría de commit relacionados con ese identificador.
5. Escritura conjunta de la resolución y del evento `local.checkout.intent_resolved_uncommitted`
   en `local_audit_events`. Un fallo de auditoría revierte todo y conserva el bloqueo.
6. El ID resuelto queda conservado en el journal y **no puede reutilizarse**. El store de
   checkout rechaza también a un proceso suspendido que intente confirmar ese ID después.

La supervisión debe dejar constancia del efectivo físico. La **ausencia de registros en
SQLite por sí sola no demuestra que nadie haya recibido dinero**; esta decisión sigue
sujeta a aprobación humana antes de operar en comercios reales.

### Pruebas automáticas de regresión

- Supervisor autorizado resuelve sólo cuando no hubo commit ni efectos financieros y confirmó la inspección física.
- Cajero original o un supervisor sin permisos suficientes no puede resolver.
- Si hay venta persistida, la resolución se rechaza sin mover el ledger.
- Falla la auditoría: rollback de resolución, intención continúa pendiente.
- Un worker antiguo no puede crear una venta luego de resolver su ID.
- WPF vuelve a habilitar carrito sólo tras verificar que no existen otras intenciones pendientes.

## Riesgos abiertos

- Resolución supervisada con RBAC, motivos y evidencia referenciada: **implementación inicial en AC-DEV**. Falta prueba de autorización independiente y UAT física; no está certificada.
- Simulación de terminación abrupta del proceso Windows real y validación hardware: **pendiente**.
- Pruebas de recuperación deben contar por separado cobertura CI y aceptación operativa. El CI verde no equivale a GO comercial.
