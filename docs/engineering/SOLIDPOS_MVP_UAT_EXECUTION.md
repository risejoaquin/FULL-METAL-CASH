# SolidPOS — Plan de aceptación operativa MVP comercial

**Alcance:** Windows WPF, una terminal / tienda, efectivo MXN, SQLite local y servidor descartable en pruebas. **Branch:** `AC-DEV`. **Estado inicial:** NO-GO. No usar datos ni fondos reales de terceros.

## Evidencia y regla de puntuación

La matriz v2 sólo acredita `71/100` puntos mediante TRX automatizados. Los nueve criterios manuales suman los otros `29/100`; ningún criterio manual se da por aprobado sin acta, versión y evidencia física. Un punto sin prueba queda en pendiente, nunca en PASS.

**Último baseline verificado antes de estas modificaciones:** `2882b14b`, 403 ejecuciones TRX, 29/29 escenarios automatizados aprobados. Al ejecutar este plan verificar el SHA exacto del candidato en GitHub Actions, no extrapolar resultados de commits antiguos.

## Condiciones de laboratorio

- Windows real (sin desplegar a producción) y carpeta SQLite exclusiva del laboratorio; mantener archivo, WAL y SHM; controlar acceso.
- Inventario de laboratorio con al menos dos SKU activos, variante, cambio de precio y SKU inactivo.
- Cajero y supervisor separados, permisos explícitos y credenciales temporales.
- Impresora de tickets compatible, papel, simulación de desconexión y reconexión. Si no existe driver o dispositivo validado, registrar BLOCKED.
- PostgreSQL desechable o stub local para ACK remoto; jamás atribuir ACK al simple guardado en SQLite.
- Actas guardadas fuera de la DB; redacción de credenciales, secretos y PII.

## Procedimientos obligatorios — 9 escenarios

| ID | Puntos | Pasos esenciales | Evidencia y PASS exigido |
| --- | ---: | --- | --- |
| START-05 | 2 | Instalar en Windows limpio, provisionar terminal y reiniciar | Registro de versión/instalador, fotos/capturas, primer arranque sin usuario demo; SQLite y WAL sobreviven |
| AUTH-05 | 3 | Acceder cajero/supervisor, cerrar sesión, probar contraseña incorrecta, ventana offline vencida y otra tienda | Ningún permiso excedido; capturas y eventos de auditoría |
| CAT-05 | 3 | Preparar dos SKU y variante, alterar cantidades/precio, probar inactivo | Carrito y precio consistentes; producto inválido rechazado; trazas de SKU |
| SALE-05 | 5 | Abrir caja, cobrar carrito por efectivo, validar cambio, simular reintento, inspeccionar ledger | Una sola venta, un pago, una línea de caja, un ID estable y total correcto |
| PRN-03 | 5 | Imprimir, desconectar impresora, recuperar y reimprimir ticket sin repetir cobro | Papel legible, cola y estado correctos, cero nuevas ventas/movimientos de caja |
| SYNC-04 | 3 | Vender offline, restablecer conexión a servidor desechable y esperar ACK | Un evento v4, un registro remoto correlacionado, ACK persistido; sin duplicados |
| CASH-04 | 4 | Apertura, varias ventas, retiro autorizado, arqueo con diferencia, cierre y reinicio | Balance sustentado en ledger, permisos verificados, cierre auditado |
| REC-06 | 2 | Terminar violentamente proceso WPF antes y después del commit, iniciar otra vez | Registro pendiente sin commit bloquea; commit íntegro se concilia sin doble cobro; impresora/outbox separados |
| REC-07 | 2 | Segundo supervisor verifica caja física, ingresa motivo y referencia, resuelve sólo intención sin venta | Denegar al cajero y a roles insuficientes; resolución y auditoría atómicas; mismo ID nunca reutilizable |

## Acta y trazabilidad por ejecución

Para cada ID: SHA, run CI, fecha local, dispositivo/driver, operador, revisor independiente, condiciones iniciales, pasos/entradas, resultado esperado, observado, estado `PASS|FAIL|BLOCKED|NOT_RUN`, evidencias, defectos vinculados, reejecución y firma de aceptación. Usar [plantilla UAT](SOLIDPOS_MVP_UAT_EVIDENCE_TEMPLATE.md).

**Bloqueo P0/P1 = NO-GO**, incluso si el porcentaje ponderado fuera 100%. Los pasos de impresora, servidor remoto y Windows físico siguen pendientes de realizar, no se infieren de los tests de unidad.

## Cierre release

1. CI del SHA candidato con todos los tests obligatorios aprobados.
2. Los nueve escenarios manuales completos y firmados en el mismo candidato.
3. Sin errores P0/P1 abiertos, evidencias reproducibles y permisos/revisión separados.
4. Procedimiento de backup/rollback, restauración y contingencia probado.
5. Autorización expresa del Product Owner para cualquier merge o despliegue, después de GO/NO-GO.

Si alguno falla, documentar hallazgo y corregir en `AC-DEV` antes de nueva revisión. No alterar `main` ni Railway para el ciclo de pruebas.
