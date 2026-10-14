# SolidPOS — Roadmap de MVP comercial, primera venta y evolución de producto

**Propuesta operativa 2026-10-14 · NO implica autorización de implementación o cambio de alcance congelado.**

## Posicionamiento y meta
POS de escritorio/servidor para comercios, con continuidad offline-first; vender una solución de caja operativa, NO un prototipo de infraestructura.

**Cliente inicial:** Dueño de comercio minorista con una caja y necesidad de registrar ventas, cobros, stock y cierre diario.

**Problema comprado:** Evitar errores al cobrar o cerrar caja; conservar operaciones tras caídas o reinicios; facilitar operación del personal.

**Oferta a validar:** Implementación SolidPOS para una caja y un comercio, con instalación, parametrización, capacitación, respaldo y soporte inicial. Precio de instalación + cuota de mantenimiento o suscripción administrada como hipótesis a validar, nunca publicada sin análisis de costos.

## Fuentes canónicas existentes (leer antes de trabajar)
- `AGENTS.md`
- `docs/COMMERCIAL_MVP_EXECUTION_PLAN.md`
- `docs/ITERATION_STATE.md`

**Lo que ya sabemos / validación pendiente:** El repositorio preserva PosServer .NET/PostgreSQL, PosCore/WPF/SQLite, dashboard, RBAC y contrato sync schema_version_4; no inferir aceptación comercial por README ni CI aislado. PR #13 abierto. Deben revisarse la auditoría y la evidencia histórica del piloto antes de repetir pruebas.

## Roadmap por puertas (sin porcentajes inventados)
### P0 — Baseline y matriz comercial
**Qué hacer:** Mapear código + tests + auditoría y PR #13 contra venta, cobro, caja, stock, usuarios, ticket, devolución, recuperación, backup; registrar evidencia por SHA y defectos críticos.

**Puerta de salida / Definition of Done:** Mapa trazable de casos de negocio, CI SHA vigente y lista cerrada de P0/P1. No asignar porcentaje sin matriz y evidencia.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

### P1 — Núcleo cobrable
**Qué hacer:** Completar ventas y pagos sin doble registro; idempotencia; recibos, inventario, transacción durable, conciliación y reversas; proteger SQLite local y PostgreSQL central según contrato congelado.

**Puerta de salida / Definition of Done:** Pruebas de venta→cobro→descuento stock→recibo; retries y reinicios sin doble cargo; pérdida/reconexión sin inconsistencia.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

### P2 — Operación diaria y distribución
**Qué hacer:** Corte de caja, turnos, permisos, devoluciones, respaldos/restore, instalación en Windows, soporte y manual de primera línea. No expandir CFDI/hardware sin decisión explícita.

**Puerta de salida / Definition of Done:** Pruebas por rol, turno/caja; instalación y recuperación reproducibles; fallos operativos conocidos documentados.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

### P3 — Piloto controlado
**Qué hacer:** Demo en PC real, recorrido del encargado, prueba supervisada de varios días con caja de prueba y datos autorizados, registro de incidencias/tiempos; confirmar evidencia de piloto histórico para no duplicar.

**Puerta de salida / Definition of Done:** PO acepta checklist de hardware, tres recorridos E2E críticos, plan de recuperación, soporte y riesgo residual antes de operar en cliente.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

### P4 — Primera venta/entrega
**Qué hacer:** Desde P0 prospectar comercios con dolor real; reservar demo, levantar requisitos y emitir propuesta con alcance, exclusiones, precio validado, pago, licencia, onboarding, soporte y SLA realista; piloto/uso real sólo con autorización.

**Puerta de salida / Definition of Done:** Al menos una propuesta firmada o pedido de compra, contrato y cobro trazables; instalación y capacitación confirmadas. No marcar venta con solo propuesta.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

### P5 — Producto repetible
**Qué hacer:** Convertir lo específico en configuración: alta de nuevo comercio, activación de caja/terminal, backup, actualizaciones, documentación y soporte; medir minutos de onboarding, tickets, costo y renovación.

**Puerta de salida / Definition of Done:** Segunda instalación independiente sin cambios en código del núcleo; métrica de margen de contribución y soporte por cliente.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

### P6 — SaaS u oferta madura
**Qué hacer:** Evaluar escritorio + servicio cloud gestionado por local versus SaaS central; diseñar multicomercio, facturación recurrente, observabilidad, seguridad, backups y autoaprovisionamiento sólo según demanda y ADR.

**Puerta de salida / Definition of Done:** GO si pilotos pagados demuestran repetibilidad, aislamiento entre clientes y rentabilidad neta positiva; no asumir que SaaS es obligatorio.

**Evidencia mínima:** issue/PR, SHA, prueba E2E o manual vinculada, responsable y GO/NO-GO. 

## Exclusiones y control de alcance
No transformar el POS mínimo en ERP, franquicias multi-sucursal, hardware especializado o facturación CFDI antes de validar clientes y resolver obligaciones correspondientes.

## Métricas para decidir la siguiente etapa
Ventas exitosas E2E, errores de cobro P0, discrepancia de caja, restauración, horas de alta, propuestas, pilotos pagados, ingresos, tickets/cliente.

## Cómo usar este roadmap y el registro horario
- Este documento **complementa**, jamás reemplaza silenciosamente, las fuentes canónicas listadas. Ante conflicto: instrucciones explícitas del PO > decisiones constitucionales/contratos aprobados > evidencia GitHub actual > este plan propuesto. Cualquier cambio estratégico/contractual pasa por aprobación/ADR.
- Al inicio de cada iteración: leer este documento, las fuentes y `docs/ITERATION_STATE.md` si existe; revisar HEAD, PR, issues, CI y cambios humanos ocurridos tras el checkpoint.
- Ejecutar ciclo íntegro **dentro de la misma sesión**: reconciliar → seleccionar un P0/P1 del gate actual → diseñar mínimo → implementar sólo lo autorizado → probar → corregir → registrar evidencia → preparar próximo slice. No esperar otra hora después de planear.
- Al terminar: actualizar checkpoint append-only (ID UTC, rama/SHA, decisión, trabajo humano descubierto, requisito, pruebas y resultado real, links PR/CI, bloqueo, avance con denominador o N/D, siguiente trabajo) y hacer commit si hay permiso. Si falla escribir: declarar `CHECKPOINT NO PERSISTIDO`, dar contenido recuperable y seguir trabajo independiente; no apagar automatización por bloqueos transitorios.
- Prohibido inventar funciones terminadas, pruebas, porcentajes o ventas. Una suite CI verde no equivale a aceptación comercial ni a aprobación de despliegue. Cambios en ramas aisladas; no force-push, merge, producción, accesos reales, secretos, gastos o despliegue sin autoridad del PO.

## Indicadores y puertas de negocio
- Etapas: `DEV` → `MVP_CANDIDATE` → `DEMO_READY` → `PILOT_AUTHORIZED` → `FIRST_PAID` → `REPEATABLE` → `SAAS_EVALUATION` (SACL usa primero `INTERNAL_GO` y `INTERNAL_VALUE`). No marcar avance por volumen de tickets o commits.
- Para cada salida anotar: cliente/arquetipo, caso de negocio, prueba automatizada + humana, evidencia SHA/PR, decisión GO/NO-GO, entregable comercial, dependencia humana y métrica real.
- Trabajo comercial desde la primera etapa: identificar demanda ACTIVA, guion de demo, contactos calificados, hipótesis de precios a validar, propuesta/alcance, objeciones, soporte, privacidad, impuestos y pagos aplicables. La IA puede preparar materiales y propuestas; contacto, publicación, contratación y pagos necesitan la decisión/autoridad correspondientes.
- Revisar el avance comercial semanalmente. Priorizar entrega real de valor y clientes pagadores antes de arquitectura SaaS; no prometer rentabilidad sin costos reales y facturación.

## Primer ciclo a realizar en GitHub
1. Conciliar este documento con el HEAD actual y con los PR abiertos, preservando el trabajo humano paralelo.
2. Construir matriz `REQUIREMENT | CURRENT PROOF | P0/P1 | TEST | COMMERCIAL GATE | OWNER | LINK`, sin declarar porcentajes N/D como cero.
3. Seleccionar el P0 permitido más cercano a `DEMO_READY` o `FIRST_PAID`; ejecutar cambios/pruebas autorizadas en este mismo ciclo. Si hay un bloqueo de autoridad, avanzar trabajo permitido de demo, QA, documentación o propuesta.
4. Registrar el checkpoint sin usar métricas estimadas como hechos.
