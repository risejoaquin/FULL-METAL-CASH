# SolidPOS PILOT-04 Hotfix 04.1 — Email Receipt Stub Status Contract

## Estado
PENDING USER VALIDATION

## Causa
El validador de PILOT-04 esperaba `queued`, `stub_queued` o `sent` para el endpoint:

`POST /api/v1/receipts/{saleId}/email`

El contrato productivo real devuelve `queued_stub`.

## Corrección
Se actualizó `scripts/pilot/validate-receipts-returns-refunds.ps1` para aceptar los estados operativos válidos:

- `queued`
- `queued_stub`
- `stub_queued`
- `sent`

## Impacto
No cambia backend, DB, endpoints, ventas, recibos, devoluciones, refunds, caja, inventario ni auditoría. Es un ajuste del validador para alinearlo con el contrato real observado en producción.

## Validación
Ejecutar nuevamente:

```powershell
.\scripts\pilot\validate-receipts-returns-refunds.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```
