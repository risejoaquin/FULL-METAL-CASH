# SolidPOS / Oasis — Plan de 4 semanas hacia primera entrega comercial estable

> **Propuesta de planificación, no compromiso comercial ni GO de producción.** Fecha base 2026-10-14. Fin del calendario base: **2026-11-10**. Rama documental: `docs/commercialization-roadmap-20261014` (PR #14 a AC-DEV).
> Este archivo no autoriza ni inicia contacto con Oasis, uso de marca, compra de hardware, cobros, despliegues ni cambios contractuales. El negocio es un prospecto sin necesidad confirmada.

## 0. Estado realmente constatado y referencias
- `AC-DEV` posee PosServer (.NET/PostgreSQL), PosCore (WPF + SQLite), PosBuilder (WPF branding/package), PosDashboard, mecanismos de cobro efectivo, caja y recuperación. Contratos y comportamiento se verifican en código, no se extrapolan de README.
- La UI WPF **de producción** dispone de login, carrito por SKU, apertura/cierre turno, cobro en efectivo y recuperación; pestañas de simulación se ocultan en producción. La interfaz rápida táctil, variantes de bebidas y manejo de extras requieren verificación/adaptación.
- PosBuilder ya configura tenant/app, colores, logo y encabezado/pie de recibo y genera/valida paquetes. No afirmar que gestiona recetas completas.
- `docs/engineering/solidpos-mvp-acceptance-matrix.json` contiene 100 puntos (71 automáticos y 29 manuales **por clasificación**, no 71 puntos aprobados).
- PR #13 (AC-DEV→main) está Draft; CI del SHA consultado anteriormente pasó, pero sólo prueba lo ejecutado en CI. Validar de nuevo al inicio de cada ciclo.
- Referencias de mayor autoridad: `AGENTS.md`, `docs/COMMERCIAL_MVP_EXECUTION_PLAN.md`, `docs/engineering/solidpos-mvp-acceptance-matrix.json`, `docs/ITERATION_STATE.md`, contratos frozen schema_version_4, y `docs/COMMERCIALIZATION_ROADMAP.md` en esta rama.

## 1. Definición negociable del MVP de UN local de bebidas
**Inclusión mínima para piloto:** catálogo con categorías y precios; tamaños y toppings configurados con precios correctos; carrito y cantidad; usuario autorizado; apertura y cierre de turno; **cobro efectivo** con cambio; venta y pago durables/idempotentes; recibo; tickets de preparación si el negocio los necesita; historial básico y consulta de caja; SQLite resiliente; respaldo y restauración; fallos no generan falsos cobros/ACK; manual y soporte inicial.
**Depende de descubrimiento:** transferencia registrada, impresora térmica concreta, tickets separados para barra, promoción simple, receta por ingrediente, devolución y anulación, modalidad offline y stock detallado. Se incorporan al MVP sólo con aceptación de alcance y pruebas pertinentes.
**No incluidos sin aprobación:** lector bancario/SDK, CFDI, nómina, contabilidad completa, sistema de puntos, delivery, ERP, múltiples sucursales, microservicios nuevos, facturación SaaS, integraciones externas no simuladas.
**Riesgo inicial:** foto de Instagram no demuestra demanda ni requisitos. Usar datos e identidad **ficticios** hasta obtener permiso. El PO valida necesidades de caja, cantidades de terminales, hardware y métodos de pago.

## 2. Roadmap base de 28 días con fechas (14 oct a 10 nov)
Cadencia: las actividades diarias son bloques de trabajo con dependencias y criterios de evidencia, **no garantía de finalización por fecha**. Si un gate no pasa, priorizar seguridad y actualizar forecast.

### Semana 1 · 14–20 octubre · Auditoría real, alcance y demo

| Fecha | Desarrollo técnico | Evidencia/negocio |
|---|---|---|
| Mié 14 | Conciliar HEAD AC-DEV, PR #13/#14, matrices y defectos P0/P1; inventario de PosCore WPF, PosBuilder, SQLite, API, impresora y artefactos. Crear matriz Oasis existente/falta/bloqueado. | QA inicial y decidir alcance con el cliente: métodos de pago, impresora, menú, tamaños/extras, conexión y quién usa caja. |
| Jue 15 | Mapear flujos PosCore: inicio de sesión → carrito SKU → turno → cobro → recibo → sync; identificar elementos sólo demo; test fixtures reproducibles. | Preparar guion de demo y preguntas al encargado; sin enviar mensajes externos sin decisión PO. |
| Vie 16 | Crear catálogo ficticio Oasis (bebidas, tamaños, toppings y precios marcados de ejemplo); configuración PosBuilder sin datos o logos reales no autorizados. | Recorrido compra/operador con datos simulados y listado de necesidades por confirmar. |
| Sáb 17 | Adaptación UI táctil: categorías y selección rápida sobre carrito/transacción existente; bloquear cambios de carrito durante cobro. | Prueba UI + regresión local SQLite (sin perder atomicidad). |
| Dom 18 | Implementar variantes, extras y cálculo server/local autoritativo donde contratos lo permitan; sin inventar stock de ingredientes terminado. | Demo preliminar de frappé/boba ficticio con modificaciones y total correcto. |
| Lun 19 | Impresión: ticket de venta y especificación de preparación opcional; estudiar hardware disponible; fallos no convierten venta en impresa. | Contract tests recibo/cola/reintento; material visual de demostración. |
| Mar 20 | Corregir regresiones y empaquetar demo; revisar permisos, accesibilidad básica, rendimiento de caja y trazabilidad MVP-01..08. | Gate Semana 1: demo reproducible, sin P0 nuevo, alcance de piloto firmado por PO. |

**Gate:** DEMO_READY condicional: una venta simulada de bebida con variantes, caja y ticket queda demostrada en Windows o demo controlada, sin fingir integración real.

### Semana 2 · 21–27 octubre · Núcleo operativo y pruebas de estrés

| Fecha | Desarrollo técnico | Evidencia/negocio |
|---|---|---|
| Mié 21 | Checkout efectivo: persistencia ACID venta/lineas/pago/ledger/auditoría/cola ticket y cambio; comprobar excepción y rollback. | Automatizar escenarios MVP-04; enlazar test real y commit. |
| Jue 22 | Cobro incierto, reintento ID único, reinicio en SQLite WAL; reconciliar evidencia faltante antes de desbloquear. | Pruebas recuperación MVP-08 y análisis de fallas P0. |
| Vie 23 | Caja y turnos: apertura, permisos, movimientos, conciliación, cierre, diferencias y auditoría. | MVP-07 unit/integration y recorrido manual controlado. |
| Sáb 24 | Catálogo, variantes, agotados, modificación de precio; stock de bebidas o insumos mínimo según necesidad confirmada. | No sobreventa, precios consistentes y cambios auditados. |
| Dom 25 | Sincronización offline a PosServer/ACK, retries e idempotencia remota; pruebas con API simulada y PostgreSQL efímero de CI cuando aplique. | MVP-06 con pérdida/red intermitente y trazas de eventId. |
| Lun 26 | Impresión física si hardware disponible: recibo, fallo, reintento, papel agotado y ticket de preparación; si no hay equipo, registrar puerta hardware pendiente. | Pruebas manuales MVP-05; no certificar desde CI headless. |
| Mar 27 | E2E operador completo: login, venta de 2 bebidas con extras, efectivo/cambio, ticket, turno y recuperación ante cierre inesperado. | Gate Semana 2: candidato piloto, sin P0 abierto, CI por SHA, checklist de aceptación. |

**Gate:** MVP_CANDIDATE: flujos transaccionales y de recuperación con CI verde en SHA vigente, sin resultados falsos ni dependencias de producción.

### Semana 3 · 28 octubre–3 noviembre · Piloto y ajustes comerciales

| Fecha | Desarrollo técnico | Evidencia/negocio |
|---|---|---|
| Mié 28 | Instalación en Windows limpio con datos de prueba, activación/binding tenant, backup/restore y documentación para encargado. | MVP-01/02 manual; tiempos reales de alta y recuperación. |
| Jue 29 | Inicio del piloto supervisado autorizado: recorrido representativo de apertura, pedidos, cobro, ticket, cierre; sin cobros reales sin autorización. | Día piloto 1 de 3: incidencias y conteo; PO presente o delegado. |
| Vie 30 | Piloto supervisado día 2: variantes, stock agotado, fallas red, reinicio, duplicados, cambio de operador y anulaciones autorizadas. | No dobles cobros ni pérdida de venta; documentar pruebas fallidas. |
| Sáb 31 | Piloto supervisado día 3: cierre de caja, conciliación, impresora, devoluciones cuando estén en alcance y backup/restauración. | Evidencia 3 días consecutivos si realmente se realizaron; no convertir una simulación en evidencia de sitio. |
| Dom 1 nov | Triage incidentes del piloto y reparación de P0/P1; separarlos de mejoras UX opcionales. | Retest focalizado con SHA final. |
| Lun 2 | Pulido UI y velocidad de operación: botones grandes, categorías, modificaciones, confirmaciones legibles, mensajes de error. | UAT operador independiente con casos sin ayuda técnica. |
| Mar 3 | Formalizar resultados del piloto y candidate release; revisar responsabilidades de pagos, datos, soporte e incidentes. | Gate Semana 3: acta aceptación técnica o NO-GO con lista cerrada. |

**Gate:** PILOT_ACCEPTANCE: Windows y equipo real autorizados, piloto supervisado con incidentes registrados; no operar cliente sin permiso explícito.

### Semana 4 · 4–10 noviembre · Estabilización y primera entrega

| Fecha | Desarrollo técnico | Evidencia/negocio |
|---|---|---|
| Mié 4 | Congelar features, resolver defectos P0/P1 y fijar SHA candidato; endurecer logs, privacidad y seguridad en instalación. | Regression suite + audit trail, rollback y sin comprometer secretos. |
| Jue 5 | Preparar instalador/release artefact, paquetes PosBuilder por comercio, guías de upgrades y recuperación. | Instalación en Windows limpio, verificación integridad y rollback probado. |
| Vie 6 | Repetir pruebas físicas y escenarios de error; cerrar aceptación matriz MVP-01..MVP-08 con casos manuales reales. | 100/100 solo cuando todas las evidencias requeridas existan y pasen; sin porcentajes inventados. |
| Sáb 7 | Paquete comercial: alcance y exclusiones, cotización conforme costos, licencia/condiciones, privacidad, soporte, capacitación y horario de atención. | Propuesta revisable; contacto/contrato y precio sujetos al PO. |
| Dom 8 | Capacitación al encargado y operador en entorno de prueba: venta, descuentos permitidos, errores, caja, backup y soporte. | Checklist de capacitación firmado si hay participante real; si no, contenido listo. |
| Lun 9 | Revisión GO/NO-GO con PO: resultados E2E y hardware, riesgos, aceptación, contrato, respaldo, canal de soporte y plan de rollback. | GO expreso del PO o NO-GO. No despliegue automático. |
| Mar 10 | Entrega estable v1.0 solamente si GO: instalación/activación acordada, handoff, garantía/soporte inicial y reporte de operatividad. Si no, release candidate documentada. | Aceptación cliente y evidencia de instalación/primer cobro acordado; abrir ciclo mantenimiento. |

**Gate:** COMMERCIAL_GO: sólo PO autoriza instalación/despliegue/uso real tras evidencia y condiciones comerciales. Si falla puerta, entregar candidato y plan de corrección, no declarar versión estable.

## 3. Dependencias y puertas de aceptación

| Gate | Condición no negociable | Evidencia |
|---|---|---|
| G0 Alcance | Entrevista o hipótesis explícitamente marcada con datos ficticios; hardware/métodos y variantes conocidos | Ficha de descubrimiento / supuestos abiertos |
| G1 Demo | UI operable de bebidas; producto→variantes→total→checkout **simulado o autorizado**; sin operaciones reales no aprobadas | Video/screen, build SHA, tests |
| G2 MVP candidato | Venta efectiva atómica; idempotencia, caja, ticket, recovery, auth e inventario consistente; CI relevante sin P0 | Casos automatizados + PR/artefactos y reporte por SHA |
| G3 UAT hardware | Instalación Windows, impresora y fallback, caja, reinicio/restore, operador; 3 días de operación piloto si acordados y efectivos | Planilla firmada, 29 puntos manuales evidenciados donde aplique, incidencias resueltas |
| G4 GO comercial | PO autoriza explícitamente riesgo residual, software, hardware, cliente, datos, cobros, costos, soporte, rollback y entrega | Acta GO/NO-GO con alcance y SHA final |
| G5 Entrega y mantenimiento | Cliente acepta instalación/entrega real y conoce contacto de soporte; backups probados | Acta de entrega, guía, tickets y métricas semana 1 |

**Aceptación funcional base:** MVP-01 Startup (10), MVP-02 Auth/RBAC (12), MVP-03 Catálogo/Carrito (13), MVP-04 Cobro (20), MVP-05 Ticket (10), MVP-06 Sync (10), MVP-07 Caja (10), MVP-08 Recovery (15). Total 100. NO calcular 71% a partir del peso automático; sólo puntuar pruebas PASS vinculadas a SHA y manuales verificadas.

## 4. Cronograma acelerado condicionado por humano + automatización horaria

### Capacidad y supuestos realistas
- Una automatización SolidPOS por hora: **24 disparos/día**; asumiendo 10–12 minutos ejecutados por disparo serían **4–4.8 horas agregadas diarias de procesamiento de agente**, si efectivamente corre cada uno. No equivale a 4–4.8 h de ingeniería humana ni garantiza 24 PR.
- Las cuatro automatizaciones del portafolio equivalen a 96 oportunidades diarias compartidas entre productos; **no adjudicar las otras 72 a SolidPOS**.
- El PO declara 10 h/día asistidas por IA **en todo SolidBit**. Base conservadora asignada a SolidPOS: **4–6 h/día** durante el piloto (112–168 h en 28 días si efectivamente se cumplen). Escenario prioritario opcional: 7–8 h/día temporalmente; no presupongas 10 h sólo para POS sin decisión del PO.
- Priorizar uso de agentes en CI, mocks, regresiones, docs y UI. Trabajo físico con impresora, entrevista, capacitación, cobro autorizado y aprobación GO requiere persona y tiempos de calendario.

| Hito | Fecha plan base | Fecha acelerada objetivo (condicional) | Aceleración válida sólo si… |
|---|---|---|---|
| Alcance confirmado + defectos críticos inventariados | 16 oct | 14–15 oct | Acceso al repo/CI y agenda del negocio |
| Demo Oasis con datos ficticios | 20 oct | 16–18 oct | UI y variantes funcionen sin mezclar demo con producción |
| MVP candidato para piloto | 27 oct | 20–23 oct | Pruebas transaccionales + recuperación PASS sin P0 |
| Hardware/UAT supervisada | 28 oct–3 nov | 23–29 oct | Equipo disponible, autorización y 3 jornadas reales para observar operación |
| Release candidate estable | 6 nov | 30 oct–3 nov | Piloto PASS, fallos cerrados, cero P0/P1 bloqueantes |
| Primera entrega estable si GO cliente | 10 nov | **3–6 nov** | Contrato/aceptación/entorno, impresora, seguridad y GO del PO |

**Forecast:** objetivo acelerado **20–24 días naturales** para una entrega estable condicional; **28 días** como calendario base. Una demo puede salir en 3–5 días. No prometer entrega antes de superar pruebas manuales, aun si sobran ejecuciones de IA.

### Trabajo continuo sin pisar al Product Owner
1. Al iniciar cada hora: leer `AGENTS.md`, roadmap comercial, matriz y `docs/ITERATION_STATE.md`; buscar commits humanos posteriores al último checkpoint, ramas, PR y CI; seleccionar un P0/P1 aún no resuelto.
2. Seleccionar trabajo desacoplado: casos de test, UI táctil, fallos de resiliencia, configuración marca, documentación. Crear rama aislada desde SHA comprobado, no `main`; limitar una modificación concurrente por archivo/dominio.
3. Dentro del **mismo** ciclo ejecutar diseño mínimo→código→test→corrección→retest→PR si está permitido. Reportar tests reales separados de no ejecutados; no dejar el plan para la siguiente hora.
4. Actualizar checkpoint con fecha UTC, SHA base/final, sprint, P0/P1 cerrado, suite ejecutada y resultado, CI HEAD, cambios PO reconciliados, gate, bloqueo, siguiente trabajo. Releer SHA antes de escribir; `CHECKPOINT NO PERSISTIDO` si falla y no deshabilitar la tarea.
5. PO dirige requisitos de Oasis, UX, hardware, decisiones comerciales, revisa PR y autoriza aceptación. Demos/contacto/piloto y producción sólo con permiso.

### Distribución diaria propuesta del PO (dentro de sus 10 horas totales)
| Bloque | Horas SolidPOS | Qué produce |
|---|---:|---|
| Revisión matutina de CI, PR y checkpoint | 0.5 | Priorización y corrección de concurrencia |
| Desarrollo guiado de UI e integración | 2.5 | Una o más capacidades completas para bebidas |
| QA UAT, terminal, impresora o entorno de prueba | 1.5 | Evidencia física que CI no puede reemplazar |
| Descubrimiento/venta/demostración | 0.5 | Hipótesis de necesidad validada y propuesta |
| **Subtotal SolidPOS** | **5.0** | Restan 5 h para los otros tres productos/operación |

## 5. Gestión de riesgo, precio y cliente
- **Demanda:** contactar únicamente si el PO decide; confirmar si ya usan POS, qué problema tienen, hardware, tarjetas/transferencias, volúmenes, tamaños/toppings y recetas. Si no hay necesidad, usar Oasis como arquetipo y prospectar otros clientes demandantes.
- **Cobro seguro:** nunca inferir cobro desde UI/ACK; bloquear doble cargo, estados financieros ambiguos y falta de ledger. Mantener `schema_version_4`, multitenancy y autorización.
- **Impresora:** reservar hardware y tiempo de pruebas temprano; si no existe impresora, demo con PDF/cola simulada pero NO declarar impresora física certificada.
- **Datos de cliente:** no copiar fotos, logo, menú o nombre a la aplicación publicada sin permiso; prototipo sólo ficticio.
- **Comercial:** definir instalación, mensualidad de mantenimiento o suscripción como hipótesis, con costos y soporte reales. Evitar vender características no implementadas.
- **Entrega:** no despliegue real/merge por automatización ni uso de producción sin GO explícito. Si 10 nov no se pasa el gate, el entregable honesto es RC + incidencias y nueva fecha.

## 6. Artefactos mínimos de primera entrega
- Installer Windows reproducible/versión fija y guía de rollback; configurador PosBuilder y assets autorizados; catálogo configurado; demo y guion.
- QA: matriz aceptación con casos automáticos y manuales, trazabilidad SHA, logs sin secretos, reporte de los 3 días del piloto cuando realmente ocurran.
- Manual rápido: apertura, agregar bebida/toppings, cobro, imprimir, cierre, no red, reinicio, rollback, respaldo/restauración.
- Paquete de servicio: alcance/exclusiones, onboarding, capacitación, precio validado, canal de soporte, tiempos de atención, control de incidentes y primer corte semanal.

## 7. Reglas de replanificación
- Semanal: cambiar `DONE` sólo por evidencia PASS; tareas parcialmente resueltas se mantienen abiertas. Fecha acelerada es objetivo condicional, no obligación de despliegue.
- Cualquier P0 financiera, corrupción de datos, RBAC, recuperación, ticket/caja o compromiso de seguridad retrasa GO. Actividad comercial de prospección puede seguir.
- Los agentes no hacen merge, despliegue, uso real, gastos, cambios de secretos, envío comercial externo ni cambios de arquitectura/contratos congelados sin autorización PO.

## 8. Próxima acción verificable (Día 1)
Inspeccionar el diff y checks actuales de AC-DEV PR #13 y documentación de PosCore/PosBuilder, capturar matriz `EXISTE / TEST PASS / MANUAL PENDIENTE / GAP`, confirmar P0 recovery y preparar demo de bebidas con datos ficticios. Registrar checkpoint, no confundir plan con ejecución.
