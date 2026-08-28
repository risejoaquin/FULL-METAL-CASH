# SolidPOS Iteration 02 — POS Operational Completion API

## Estado de la entrega

**Estado:** entregable listo para validación local/remota por el usuario.

Esta iteración deja el backend POS más cerca de operación diaria completa, con foco en venta real, caja/corte, recibo digital, reportes operativos y validación E2E sobre tenant productivo.

## Objetivos cubiertos

- Venta productiva sobre tenant real.
- Cash shift / corte de caja con resumen operativo verificable.
- Endurecimiento de reportes de caja contra ventas, pagos, refunds y movimientos.
- Seed operativo mínimo para tenant productivo existente.
- Bootstrap productivo ahora deja datos POS mínimos para operar una venta inicial.
- Contrato OpenAPI actualizado.
- Migración 017 idempotente.
- Scripts operativos para seed y validación E2E.
- Tests unitarios de resumen operativo de caja.

## Cambios principales

### 1. Nuevo resumen operativo de cash shift

Endpoint nuevo:

```http
GET /api/v1/cash-drawers/shifts/{shiftId}/summary
```

Permiso:

```text
reports.cash_shift_summary
```

Devuelve:

```text
openingAmountCents
expectedCashCents
countedCashCents
differenceCents
cashSalesCents
nonCashSalesCents
cashRefundsCents
nonCashRefundsCents
cashInCents
cashOutCents
noSaleDrawerOpenCount
salesCount
returnsCount
movementCount
openedAt
closedAt
```

### 2. Migración 017

Archivo:

```text
database/postgresql/017_pos_operational_completion.sql
```

Agrega/endurece:

```text
cash_movements.occurred_at
cash_movements.local_occurred_at
cash_movements.metadata
idx_cash_movements_shift_occurred
idx_sales_cash_shift_status
idx_returns_cash_shift_status
reports.cash_shift_summary
```

### 3. Seed operativo para tenant productivo

Script:

```text
scripts/operations/seed-production-pos-runtime.ps1
scripts/operations/seed-production-pos-runtime.sql
```

Crea o actualiza datos mínimos:

```text
unit family: quantity
unit: unit
category: Bebidas
product: QSR-AMERICANO / Americano 12oz
price list: default
price: 4500 MXN
payment methods: cash, card_manual, transfer
```

### 4. Validación E2E de venta real

Script:

```text
scripts/operations/validate-production-pos-e2e.ps1
```

Flujo:

```text
login admin productivo
crear token de enrolamiento de terminal
registrar terminal
abrir turno de caja
crear venta real
emitir recibo digital
leer resumen operativo de caja
cerrar turno de caja
leer resumen final
```

## Decisión arquitectónica

Iteration 02 no elimina flujos anteriores. Los endurece:

- El tenant productivo ya no depende del usuario demo.
- La operación POS real se valida con tenant/admin/store productivos.
- La venta real requiere terminal token, no solo token de usuario admin.
- El corte de caja ahora tiene endpoint de resumen verificable.

## Comandos obligatorios

```powershell
dotnet restore solidpos-platform.sln

dotnet build solidpos-platform.sln

dotnet test solidpos-platform.sln
```

Si pasan, aplicar migraciones:

```powershell
.\scripts\apply-postgresql-migrations.ps1
```

Para Supabase/Railway real:

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"

docker run --rm `
  --env "DATABASE_URL=$env:DATABASE_URL" `
  -v "${PWD}:/work" `
  -w /work `
  postgres:16 `
  psql "$env:DATABASE_URL" -v ON_ERROR_STOP=1 -f database/postgresql/017_pos_operational_completion.sql
```

## Validación productiva recomendada

Primero seed operativo del tenant productivo:

```powershell
.\scripts\operations\seed-production-pos-runtime.ps1 `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Currency "MXN"
```

Luego E2E:

```powershell
.\scripts\operations\validate-production-pos-e2e.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

## Criterio de cierre PASS REAL

```text
restore/build/test local PASS
migración 017 local PASS
migración 017 remota PASS
Railway /health/ready PASS
smoke remoto con admin productivo PASS
seed operativo tenant productivo PASS
validate-production-pos-e2e PASS
GitHub Actions PASS
```


## Hotfix 02.1

Se agregó `sales.read` al permiso default de terminal para permitir que el flujo POS productivo emita recibo digital usando el token de terminal sin fallar con `403 Forbidden` en `POST /api/v1/receipts/{saleId}/issue`.
