# SolidPOS PILOT-05 — Offline Mode Field Test

## Estado

`PENDING USER VALIDATION`

## Objetivo

Validar en producción un flujo offline controlado de PosCore con SQLite local, sync schema version 4, outbox/inbox, push sync, idempotencia, pull sync posterior, conciliación con PosServer, recibo digital, inventario, caja y bitácora de piloto.

## Alcance

- Runtime local PosCore SQLite con WAL.
- Binding local de terminal contra terminal productiva registrada.
- Bootstrap real `/api/v1/sync/bootstrap`.
- Contrato real `/api/v1/sync/contract` con `currentSchemaVersion = 4`.
- Login/session local autorizado.
- Catálogo local desde PosServer.
- Inventario local desde PosServer.
- Venta offline cash desde caché local.
- Movimiento local de inventario desde receta cacheada.
- Outbox local `sale.completed` schema version 4.
- Reconexion y push `/api/v1/sync/push`.
- Procesamiento `/api/v1/sync/process`.
- Idempotencia por requeue del mismo `eventId` y duplicate push.
- Pull sync `/api/v1/sync/pull` dos veces para cursor/idempotencia local.
- Read models locales de venta/recibo remoto.
- Recibo digital productivo y print job local.
- Cierre local/remoto de caja con diferencia cero.
- Validación SQL productiva scoped por `tenantId`, `storeId`, `terminalId`, `batchId`, `localSaleId`, `saleId`, `receiptId`.
- Log en `docs/pilot/logs/pilot-05-offline-mode-field-test-log.md`.

## Qué se cambió

- Se agregó el validador productivo `scripts/pilot/validate-offline-mode-field-test.ps1`.
- Se agregó el validador SQL `scripts/pilot/pilot-05-offline-mode-field-test-check.sql`.
- Se agregó documentación de ejecución `PILOT_05_VALIDATION_COMMANDS.md`.
- Se agregó checklist operador `docs/pilot/pilot-05-operator-checklist.md`.
- Se agregó GO/NO-GO `docs/pilot/pilot-05-go-no-go.md`.
- Se agregó bitácora placeholder `docs/pilot/logs/pilot-05-offline-mode-field-test-log.md`.
- Se agregó comando PosCore CLI `sale-offline-from-cache-cash-with-inventory` para validar en una sola venta offline: pago cash local, cash shift local, outbox y consumo de inventario local.

## Módulos afectados

- `src/PosCore/SolidPOS.PosCore.Cli`
- `scripts/pilot`
- `docs/pilot`

## Decisión técnica

PILOT-05 queda como fase de validación end-to-end controlada, no como migración de esquema. El flujo usa contratos ya existentes de PosServer y PosCore:

- `sync schema version 4` para outbox local.
- `/api/v1/sync/bootstrap` como snapshot inicial de terminal.
- `/api/v1/sync/push` + `/api/v1/sync/process` como reconexión offline-to-online.
- `/api/v1/sync/pull` para reconciliación posterior.
- SQL final scoped por IDs de la transacción para evitar falsos NO-GO por historial del tenant.

Se agregó un comando CLI específico porque el runtime ya tenía venta cash offline y venta con inventario offline por separado. El piloto necesita validar ambos comportamientos en una sola transacción de campo.

## Riesgos

- Requiere Docker local para ejecutar `postgres:16 psql` contra Supabase.
- Requiere .NET 8 local; este entorno no tiene `dotnet`, por eso build/test no fueron ejecutados aquí.
- La contraseña se pasa al CLI local para cachear usuario offline. No se imprime en esta documentación, pero en Windows puede aparecer temporalmente como argumento de proceso durante la ejecución.
- Si el producto `QSR-AMERICANO` no tiene receta activa o `recipe_items`, el piloto debe fallar: PILOT-05 necesita validar inventario local real.
- Si `scripts/posdashboard/validate-posdashboard-operations-dashboard.ps1` falla por tooling frontend, se puede ejecutar PILOT-05 con `-SkipDashboardValidation` y validar dashboard por API/SQL.

## Resultado esperado

`SolidPOS PILOT-05 — Offline Mode Field Test = PASS REAL PRODUCTION / GO`

## GO / NO-GO

GO solo si:

- Health live/ready PASS.
- Admin login PASS.
- Sync contract reporta schema version 4.
- Bootstrap terminal PASS.
- Login local y permiso `sales.create` PASS.
- Catálogo local incluye `QSR-AMERICANO`.
- Inventario local tiene receta/items.
- Venta offline queda en outbox.
- Push sync procesa `sale.completed`.
- Duplicate push no genera dead-letter ni rejected.
- Pull sync corre dos veces sin romper cursor.
- Venta remota se materializa con `localSaleId`.
- Recibo digital queda activo.
- Inventario ledger remoto registra salida por venta.
- Shift remoto cierra con diferencia cero.
- SQL final devuelve `GO`.

## HOTFIX 05.3

Status: PENDING USER VALIDATION

Fixes PowerShell parser failure caused by Markdown triple-backtick fences in the final log writer. The validator now writes the pilot log line-by-line with ASCII-safe strings only.

## HOTFIX 05.4 note

PILOT-05 HOTFIX 05.4 corrected the production recipe SQL lookup in `scripts/pilot/validate-offline-mode-field-test.ps1`.
The validator now uses `pos.recipes.output_product_id` instead of the nonexistent `pos.products.recipe_id` column.
