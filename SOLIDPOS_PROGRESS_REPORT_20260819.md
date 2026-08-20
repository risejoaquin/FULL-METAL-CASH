# SolidPOS — Reporte de avance general

Fecha: 2026-08-19

## Estado general

SolidPOS ya tiene backend productivo desplegado, tenant productivo provisionado, flujo POS operativo, sync offline server contract, PosCore local con SQLite, push offline-to-online, retry/duplicate, procesamiento semántico de venta offline y venta desde catálogo cacheado.

## Iteraciones cerradas

| Iteration | Estado | Resultado |
|---|---:|---|
| 01 | PASS REAL | Production Tenant Provisioning + Admin Bootstrap |
| 02 | PASS REAL | POS Operational Completion API |
| 03 | PASS REAL | Offline Sync End-to-End Server Contract |
| 04 | PASS LOCAL REAL | PosCore Local Foundation + SQLite Offline Runtime |
| 05 | PASS REAL | PosCore Offline-to-Online Sync Runtime |
| 06 | PASS REAL | PosCore Sync Processing + Conflict/Retry Runtime |
| 07 | PASS REAL | PosCore Offline Sale Semantic Processing |
| 08 | PASS REAL | PosCore Local Catalog/Inventory Cache |

## Capacidades ya verificadas

- PosServer corre en Railway contra PostgreSQL/Supabase.
- Tenant productivo real creado.
- Admin productivo real operativo.
- Terminal enrollment y terminal token funcionando.
- Venta remota directa funcional.
- Recibo digital funcional.
- Cash shift open/close/summary funcional.
- Sync contract schemaVersion 4 operativo.
- PosCore SQLite local inicializa con WAL.
- PosCore genera ventas offline locales.
- PosCore genera outbox local.
- PosCore hace sync push contra PosServer real.
- PosCore maneja duplicate/retry/failed local.
- PosServer procesa venta offline `sale.completed` y materializa venta real.
- PosCore sincroniza catálogo remoto hacia SQLite.
- PosCore vende por SKU cacheado sin depender de ProductId manual.

## Riesgos/deudas vigentes

- Secretos expuestos durante pruebas: rotar Supabase DB password, Railway connection string, PROVISION_KEY y claves JWT antes de piloto real.
- PosCore aún no tiene UI WPF.
- PosCore aún no tiene auth local/RBAC offline.
- PosCore aún no tiene hardware abstraction.
- PosCore aún no tiene resiliencia completa contra apagones/corrupción local.
- Dashboard React aún no existe.

## Próxima iteración

SolidPOS Iteration 09 — PosCore Local Inventory Consumption Cache.

Objetivo: cachear recetas/BOM desde PosServer, descontar inventario local estimado al vender offline, sincronizar venta y reconciliar movimientos locales contra `pos.inventory_ledger` remoto.

## Iteration 21 — Production Security Closure

Prepared: security closure scripts, secret generation helpers, stronger local secret scan, refresh token revocation SQL, production validation script, and operational checklist. Pending user validation after rotating production secrets in Railway/Supabase.

## SolidPOS Iteration 22 — Production Pilot Readiness

Status: Prepared for validation.

Scope:

- Production pilot readiness validator.
- Pilot GO/NO-GO checklist.
- Pilot runbook.
- Pilot checklist.
- Dashboard audit route alignment to `/api/v1/audit/events`.
- Validation of liveness, readiness, login, metrics, sync, provisioning, sales, returns and audit.

Closure target:

```text
SolidPOS Iteration 22 — Production Pilot Readiness = PASS REAL PRODUCTION
```
