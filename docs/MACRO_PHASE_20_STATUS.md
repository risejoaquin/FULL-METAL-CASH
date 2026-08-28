# Macro Fase 20: Audit Read API + Admin Operational Visibility Base

## Objetivo

Exponer lectura operativa de auditoria para Dashboard/Admin sin entrar directo a PostgreSQL.

## Implementado

| Area | Implementacion |
| --- | --- |
| Endpoint | `GET /api/v1/audit/events` |
| Permiso | Protegido con `audit.read` |
| Tenant isolation | Consulta tenant-aware con `ITenantContext` y RLS PostgreSQL |
| Filtros | `action`, `entityType`, `entityId`, `actorUserId`, `terminalId`, `from`, `to` |
| Paginacion | `page`, `pageSize`, maximo 200 |
| Contratos | `AuditEventResponse`, `AuditEventPageResponse`, `AuditEventPageMetaResponse` |
| Aplicacion | `IAuditEventService`, `IAuditEventRepository`, `AuditEventFilters` |
| Infraestructura | `AuditEventService`, `PostgreSqlAuditEventRepository` |
| OpenAPI | `/audit/events` actualizado con filtros completos |
| Tests | Unit tests de validacion de tenant, rango de fechas y paginacion |

## Contrato de lectura

```http
GET /api/v1/audit/events?page=1&pageSize=50
Authorization: Bearer <owner/admin-token>
```

Respuesta:

```json
{
  "items": [
    {
      "id": "uuid",
      "tenantId": "uuid",
      "actorUserId": "uuid",
      "terminalId": null,
      "action": "admin.catalog.category.soft_delete",
      "entityType": "category",
      "entityId": "uuid",
      "beforeData": null,
      "afterData": {},
      "ipAddress": "::1",
      "userAgent": "PowerShell/...",
      "traceId": "trace-id",
      "occurredAt": "2026-08-15T23:17:13.118132+00:00"
    }
  ],
  "meta": {
    "page": 1,
    "pageSize": 50,
    "total": 1
  }
}
```

## Smoke test esperado

1. Iniciar sesion como owner.
2. Consultar `GET /api/v1/audit/events?page=1&pageSize=10`.
3. Filtrar por `action=admin.catalog.category.soft_delete`.
4. Filtrar por `entityType=category`.
5. Confirmar que los resultados coinciden con `pos.audit_events`.

## Nota de diseno

La API nunca recibe `tenantId` como filtro externo. El tenant sale exclusivamente del JWT y se refuerza con RLS en PostgreSQL.
