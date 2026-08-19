# SolidPOS Iteration 02 Hotfix 02.1 — Terminal Receipt Permission Alignment

## Estado

Hotfix preparado para corregir el fallo `403 Forbidden` durante el flujo E2E productivo al emitir recibo digital con token de terminal.

## Causa

El endpoint:

```http
POST /api/v1/receipts/{saleId}/issue
```

requiere `sales.read`, pero los tokens de terminal generados por `TerminalPermissionSet.Default` tenían `sales.create`, `sales.void`, `returns.*`, `cash.*` y `reports.cash_shift_summary`, pero no `sales.read`.

El flujo de venta productiva usa token de terminal para:

1. abrir caja,
2. crear venta,
3. emitir recibo digital,
4. consultar resumen de turno,
5. cerrar caja.

La emisión de recibo es parte operacional del POS, por lo que el token de terminal debe poder leer la venta que acaba de crear y emitir su recibo.

## Cambio aplicado

Archivo modificado:

```text
src/PosServer/SolidPOS.PosServer.Application/Terminals/TerminalPermissionSet.cs
```

Se agregó:

```csharp
PermissionCodes.SalesRead,
```

a la lista de permisos default de terminal.

## Impacto

Los nuevos tokens de terminal emitidos después del deploy tendrán `sales.read` y podrán pasar el paso de emisión de recibo del script:

```text
scripts/operations/validate-production-pos-e2e.ps1
```

## Validación requerida

```powershell
dotnet restore solidpos-platform.sln
dotnet build solidpos-platform.sln
dotnet test solidpos-platform.sln
```

Después de subir a GitHub/Railway y redeploy:

```powershell
.\scripts\smoke-test-deployment.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -Email "admin@micafeteria.com" `
  -Password "AdminSeguro123!" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde"

.\scripts\operations\validate-production-pos-e2e.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -AdminEmail "admin@micafeteria.com" `
  -AdminPassword "AdminSeguro123!"
```

Resultado esperado:

```text
Production POS E2E flow completed.
```
