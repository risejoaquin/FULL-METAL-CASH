# SolidPOS — Plan de ejecución hacia MVP comercial

> **Plan operativo con trazabilidad a código (corte 2026-10-14):** consultar [ROADMAP_MVP_COMERCIAL_CODE_AUDITADO_2026-10-14.md](engineering/ROADMAP_MVP_COMERCIAL_CODE_AUDITADO_2026-10-14.md) antes de elegir tareas. Ese documento añade inventario de código, backlog P0/P1, ruta crítica, riesgos y gates de aceptación. El presente plan general es una referencia histórica y no acredita comercialización. Nunca convertir CI o simulaciones en GO; no merge/deploy sin autorización.

Fecha base: 2026-10-13. Documento de dirección, **no** certificación de funcionalidades existentes.

## Objetivo y límite
POS mínimo comercial para una tienda operando con cobros reales.

## Etapas / sprints (secuencia orientativa; basar fechas en evidencia)
### Etapa 0
S0 Auditoría de brechas P0: mapear implementación real, auditoría AUD-08, deuda y evidencia histórica de prueba de 3 días; no repetir pruebas si evidencia es suficiente.
**Salida:** PR revisable, CI y evidencia por casos de aceptación; no asignar porcentaje sin prueba.

### Etapa 1
S1 Persistencia y venta íntegra: SQLite en pruebas, carrito, impuestos, pagos efectivo y mixtos, folios, recibos, inventario, transacciones e idempotencia.
**Salida:** PR revisable, CI y evidencia por casos de aceptación; no asignar porcentaje sin prueba.

### Etapa 2
S2 Cobros inciertos y recuperación tras reinicio: estado persistido, conciliación, doble cobro imposible, reversas, cortes de caja y cierre seguro.
**Salida:** PR revisable, CI y evidencia por casos de aceptación; no asignar porcentaje sin prueba.

### Etapa 3
S3 Operación comercial: usuarios/RBAC, catálogo, stock, devoluciones, reembolsos, impresión, offline/reconexión según alcance real.
**Salida:** PR revisable, CI y evidencia por casos de aceptación; no asignar porcentaje sin prueba.

### Etapa 4
S4 Hardening y piloto: instalar, operar varios días con datos de prueba realistas, respaldo/restauración, fallos y recuperación, manual y checklist GO/NO-GO.
**Salida:** PR revisable, CI y evidencia por casos de aceptación; no asignar porcentaje sin prueba.

## Criterio de aceptación comercial
Venta completa y persistente, cobro único tras reinicio, inventario correcto, efectivo/cuadre, rollback/cancelación, restauración verificadas mediante unit/integration/contract/E2E/fault-injection y piloto.

## Contrato de cada iteración horaria
1. Leer este plan, README, documentación constitucional/AGENTS y PRs/CI recientes. Resolver conflictos a favor de decisiones del Product Owner y contratos aprobados.
2. Inspeccionar código y tests; establecer brecha verificable sin inferir que README=implementación; consultar resultados del último ciclo y evitar repetición.
3. Tomar máximo un slice vertical P0/P1 capaz de dejar código, pruebas y evidencia en la misma iteración. Si bloqueado, avanzar en un slice independiente.
4. Codificar **sólo en rama de trabajo**; correr tests unitarios, integración, contratos y E2E cuando aplique; verificar GitHub Actions. No usar proveedores reales mientras desarrollo local/CI baste.
5. Registrar matriz de trazabilidad requisito → issue → commit/PR → prueba → resultado → evidencia; fallos nunca se contabilizan como éxito.
6. Publicar reporte Markdown en PR con Gantt de etapas, % demostrado y % no demostrado; cada porcentaje debe explicar denominador y pruebas de aceptación, no líneas de código, commits o tickets cerrados.
7. Detener modificaciones riesgosas si requiere aprobación, secretos, gastos o producción; documentar exactamente qué requiere intervención humana. Preparar siguiente slice.

## Gobernanza innegociable
- No merge, deploy, cambio de producción, ejecución sobre datos reales, rotación de secretos ni gasto sin autorización expresa.
- No sobrescribir decisiones explícitas, modelos de respuesta congelados o funcionalidades probadas para simplificar el desarrollo.
- Separar documentación de pruebas ejecutadas y de pruebas previstas; marcar cada evidencia con SHA y fecha.
- Prohibido atribuir a un ciclo horario la capacidad de retomar un chat específico: el estado oficial es GitHub (archivos, PR, issues, Actions).
- Si hay ejecuciones concurrentes, no pisar ramas ni cambios: serializar las escrituras por repo.

## Medición
Para cada requisito crítico mantener peso, criterio de aceptación, prueba automatizada/E2E, estado y SHA. `avance_validado = suma(pesos de criterios aprobados con evidencia) / suma(pesos totales del MVP)`. Desconocido != 0%; reportar **N/D** si no está inventariada la matriz. Mostrar también defectos P0/P1 abiertos, CI verde/rojo, riesgos y bloqueo GO/NO-GO.

## Rama de desarrollo de referencia
`AC-DEV`. Nunca escribir sobre `main` salvo autorización y política existente; si la rama aquí indicada es `main`, crear primero rama dedicada de feature/plan.
