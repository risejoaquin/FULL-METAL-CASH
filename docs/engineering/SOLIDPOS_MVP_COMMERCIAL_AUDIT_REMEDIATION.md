# SolidPOS — POS mínimo comercial: ejecución y cierre de auditoría

**Destino de desarrollo:** `AC-DEV`; **objetivo:** una tienda, una sucursal, una terminal Windows, cajero y supervisor. **Estado:** NO-GO para despliegue hasta aceptación completa.
**Fuentes de entrada:** `SOLIDPOS_POS_MINIMO_COMERCIAL_DISENO_v1(1).md` y `FULL_METAL_CASH_SOLIDPOS_AUDITORIA_2026-10-13(3).md`, documentos aportados por el PO el 13 de octubre de 2026. La auditoría es estática y fechada: verificar cada hallazgo contra el HEAD antes de declararlo cerrado.

## Prioridad: ciclo comercial verdadero antes que nuevas funciones

La arquitectura ya tiene SQLite, dominio offline, outbox v4, PostgreSQL y tests. El **P0 (AUD-08)** es que el shell WPF disponible para el operador cambia contadores/etiquetas sin realizar el cobro y persistencia correspondientes. Hasta que ese defecto esté cerrado, **no puede afirmarse** que la UI comercial venda.

En virtud del diseño MVP, implementar estrictamente:
- **MVP-01/W1:** arranque real de SQLite y terminal, sesión bloqueada por defecto, sin demostraciones en modo comercial. No convertir un estado visual en evidencia financiera.
- **MVP-02/W1:** Login WPF real con contraseña no almacenada, `LocalAuthService`, sesión activa/caducidad/permisos y cierre real.
- **MVP-03/W2:** catálogo activo real desde SQLite, SKU/búsqueda y carrito multítem, redondeos e identidad de artículo.
- **MVP-04/W3:** transacción única `sale + lines + payments + cash movement + inventory applicable + outbox v4 + print job + audit`, idempotente bajo doble clic y reinicio.
- **MVP-05–08/W4–W7:** recibo real, sincronización con ACK, devolución total autorizada, cierre de caja, recuperación y UAT/hardware.

`schemaVersion=4` y `syncContract=schema_version_4` permanecen congelados.

## Implementación MVP-01 en progreso

- `PosCoreStartupInspectionService` (Application) inicializa una base real y consulta binding v4, usuarios activos **del mismo tenant/store** y recuento de productos. Su resultado diferencia `terminal_unbound`, `users_missing`, `catalog_missing`, `checkout_not_connected`.
- La ruta comercial WPF utiliza `%LOCALAPPDATA%/SolidBit/SolidPOS/terminal/poscore.sqlite` y `SQLiteLocalPosRepository`, no `./.runtime`.
- La producción **oculta completamente** las pestañas QSR/recibos/sync/caja/login de simulación. Muestra un estado explícito de activación y cobro deshabilitado. Solamente el parámetro explícito `--self-test` preserva la simulación para pruebas históricas.
- Si falla abrir o inicializar la base SQLite, el shell se bloquea y el operador recibe un error de inicio sin confirmar operaciones inexistentes.
- Pruebas SQLite descartables verifican reinicio, WAL, binding incompleto/v3, usuarios ausentes o de otra tienda y que `CanCheckout` nunca se activa prematuramente.

**Esta unidad no cierra AUD-08**: bloquea el falso positivo comercial pero falta cablear el ciclo financiero real. No habilitar el botón de cobrar hasta completar MVP-04.

## Auditoría — clasificación de próximos cambios

| Urgencia | Hallazgos | Disposición |
| --- | --- | --- |
| P0 bloqueante | AUD-08 | WPF real / checkout atómico; prioridad absoluta |
| P1 seguridad/despliegue | AUD-01/02/03/04/11/19 | Rulesets y autodeploy requieren autorización; proteger configuración de proxies, frontend prod fallback, token/logout |
| P1 exactitud financiera | AUD-05/09/10/17 | cadena PG 001–021, métricas reales >10 ventas, salud `unknown`, ledger de devoluciones |
| P1 autoridad/promociones | AUD-06/07/16 | refresco de permisos autenticado; SQL propuesta no productiva; firmas de descuentos sin emisor central son solo pruebas |
| P1/P2 QA/operación | AUD-12/13/14/15/20/21/22/23/24/25/26 | health, navegador E2E, SAST, contraseñas CLI, cifrado, impresora, métricas claims, demo UX |

## Criterios que prohíben declarar GO

- Ninguna UI que aparente cobrar/abrir caja/marcar sync sin commit o ACK real.
- Nunca suma de caja usando el total de venta en cobros mixtos; efectivo real por método y transacción única.
- Las pruebas de éxito deben usar archivos SQLite desechables reales, simuladores explícitos de backend, PostgreSQL 16 **solamente efímero**, recuperación de proceso/reinicio y tests de falla/rollback.
- No mezclar `main` ni operar recursos Railway, tokens reales, hardware real o datos de negocios sin autorización separada.
- CI verde es criterio de calidad de código, no certificación del producto, autorización de release ni evidencia física.

**Completado =** código conectado en WPF + casos AC-01–AC-20 aplicables y comprobados por SHA + revisión independiente + GO explícito del PO. Las mejoras criptográficas y de descuentos permanecen secundarias mientras AUD-08 continúe abierto.
