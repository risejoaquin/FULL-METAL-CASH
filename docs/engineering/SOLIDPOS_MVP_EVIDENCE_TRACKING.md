# SolidPOS — Medición del MVP por escenarios de aceptación

**Ámbito:** rama `AC-DEV`, Windows .NET 8, SQLite local, PostgreSQL descartable. **Política:** el reporte de pruebas jamás autoriza merge, despliegue, manejo de dinero real ni GO comercial.

## ¿Qué mide el CI?

La fuente versionada es [solidpos-mvp-acceptance-matrix.json](solidpos-mvp-acceptance-matrix.json), esquema v2. Contiene 8 requisitos MVP y **38 escenarios de aceptación con pesos que suman 100**.

Cada escenario tiene ID permanente (p. ej. `REC-02`), alcance y peso:

- **Automatizado:** apunta a uno o más nombres exactos `Clase.Método`; todos deben aparecer aprobados en los TRX del SHA que se inspecciona. No se admite coincidencia por nombre de clase, parecido parcial ni existencia de test sin ejecución.
- **Manual:** contiene un procedimiento; permanece `pending_manual` hasta que haya acta de ejecución, build SHA, condiciones, operador, fecha, resultado y evidencias revisadas de forma independiente. Ninguna suite automatizada lo da por aprobado.

### Métricas y fórmula

```
verifiedAcceptanceEvidencePercent =
  SUM(weight of automated scenarios with every mapped test passing) / 100 * 100

automatedScenarioPassRateWeighted =
  SUM(weight of passing automated scenarios) / SUM(weight of automated scenarios) * 100

commercialReadinessPercent = NULL (hasta UAT completa y revisión de gates)
releaseDecision = NO-GO
```

Los **71 puntos automatizables y 29 puntos de aceptación manual** son una distribución de esfuerzo **acordada en la matriz**, no una estimación subjetiva recalculada en cada respuesta. Un 100% de pruebas automatizadas puede producir a lo sumo 71/100 puntos con esta matriz.

No confundir este resultado con cobertura de líneas de código, éxito de una pantalla o disponibilidad para vender. Un cambio de pesos, escenarios o métodos requerirá revisión trazable en PR, no ajustes para inflar un porcentaje.

### Evidencia por ejecución

CI `.github/workflows/solidpos-ci.yml` produce:
- `TestResults/**/*.trx` (resultado real del runner Windows).
- `solidpos-mvp-traceability.json` (SHA, run ID, criterios, peso, métodos exactos descubiertos, pruebas aprobadas/fallidas/ausentes, pendientes manuales).
- Artifacts con retención de 30 días para la matriz; los TRX base se conservan 14 días.

La etapa `--strict` debe fallar si cualquier método asociado a aceptación automatizada falta o no aprueba. Si TRX está vacío, el informe falla y registra 0 evidencia.

## Niveles de prueba necesarios

| Nivel | Ejemplo | Evidencia aceptable |
| --- | --- | --- |
| Unitario | Permisos, cálculos | TRX, assertion y SHA |
| Integración local | SQLite commits/outbox/caja | Transacciones en DB temporal + TRX |
| Contract test | schema v4, endpoint auth | Tests + contratos firmes |
| E2E descartable | terminal + PostgreSQL | Logs transaccionales y ACK correlacionado |
| Fault injection | matar proceso o desconectar impresora | Estado previo/posterior, IDs estables, cero dobles cobros |
| Seguridad | RBAC, tenant/store, manipulación | Denegaciones y auditoría |
| UAT operador | Jornada real simulada | Acta por caso, capturas, conteo y firma |
| Release | Instalación, backups, rollback | Evidencia de recuperación y aceptación de PO |

## Gating comercial

El GO sólo se propone cuando:
1. Todos los escenarios críticos están aprobados con evidencias del mismo build/candidato;
2. No quedan defectos P0/P1 abiertos;
3. Las pruebas de recuperación, reconciliación, idempotencia, caja y tenant aislamiento tienen resultados comprobados;
4. Se han firmado los recorridos UAT físicos (impresora, caja, offline/reconexión, reinicios);
5. Se completó revisión de rollback, backups y controles de despliegue.

**Ningún indicador ponderado cancela estas condiciones.** Con cualquier crítico abierto: NO-GO.

## Uso

```powershell
python -m unittest discover -s tests/quality -p "test_*.py" -v
python scripts/quality/solidpos_traceability.py `
  --matrix docs/engineering/solidpos-mvp-acceptance-matrix.json `
  --trx-dir TestResults `
  --commit (git rev-parse HEAD) `
  --output TestResults/solidpos-mvp-traceability.json `
  --strict
```
