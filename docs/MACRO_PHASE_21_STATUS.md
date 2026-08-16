# Macro Fase 21: Reports Runtime Base

## Objetivo

Exponer reportes operativos minimos para Dashboard/Admin usando SQL explicito optimizado para PostgreSQL.

## Implementado

| Reporte | Endpoint | Permiso |
| --- | --- | --- |
| Ventas por rango | `GET /api/v1/reports/sales/range` | `reports.read` |
| Ventas por metodo de pago | `GET /api/v1/reports/sales/by-payment-method` | `reports.read` |
| Cortes/caja por turno | `GET /api/v1/reports/cash/shifts` | `reports.read` |
| Top productos | `GET /api/v1/reports/products/top` | `reports.read` |
| Inventario negativo | `GET /api/v1/reports/inventory/negative` | `reports.read` |
| Movimientos de inventario | `GET /api/v1/reports/inventory/movements` | `reports.read` |

## Filtros

| Filtro | Uso |
| --- | --- |
| `storeId` | Filtra por sucursal cuando aplica |
| `from` | Inicio del rango; si no se envia, default ultimas 24 horas |
| `to` | Fin del rango; si no se envia, default `now()` servidor |
| `limit` | Maximo de filas para tablas; default 20, maximo 200 |

## Arquitectura

| Capa | Archivos |
| --- | --- |
| Contracts | `SolidPOS.PosServer.Contracts/Reports/*` |
| Application | `IReportsService`, `IReportsRepository`, `ReportFilters` |
| Infrastructure | `ReportsService`, `PostgreSqlReportsRepository` |
| API | `ReportsEndpoints` |
| OpenAPI | `contracts/openapi/solidpos-api-v1.openapi.yaml` |
| Tests | `ReportsServiceTests` |

## Notas de diseno

- Todos los reportes toman `tenant_id` exclusivamente desde JWT/`ITenantContext`.
- No se permite filtrar por `tenantId` desde query string.
- Las ventas consideran `status = completed` para totales operativos.
- Las ventas anuladas se contabilizan aparte en el resumen.
- Inventario negativo se calcula desde la vista `pos.inventory_stock`, derivada del ledger append-only.
- Movimientos recientes leen directamente `pos.inventory_ledger`.
- Los importes se devuelven en centavos para evitar errores de precision en frontend.
