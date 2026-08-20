# SolidPOS PILOT-02 Hotfix 02.1 — Protected Metrics Shape Compatibility

## Estado

PENDING USER VALIDATION

## Motivo

El script `scripts/pilot/validate-real-pos-transaction.ps1` validaba el endpoint protegido de métricas usando la forma plana:

```powershell
$metrics.databaseReady
```

La API productiva devuelve la forma usada por los validadores ya aprobados de seguridad, readiness y PILOT-01:

```powershell
$metrics.database.ready
$metrics.database.requiredTablesPresent
```

## Corrección

El validador PILOT-02 ahora soporta ambas formas para mantener compatibilidad:

- `metrics.database.ready` + `metrics.database.requiredTablesPresent`
- `metrics.databaseReady` como fallback heredado

## Archivos modificados

```text
scripts/pilot/validate-real-pos-transaction.ps1
SOLIDPOS_PILOT_02_HOTFIX_02_1.md
```

## Validación

Re-ejecutar solo:

```powershell
.\scripts\pilot\validate-real-pos-transaction.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -StoreCode "MAIN" `
  -ProductSku "QSR-AMERICANO" `
  -PaymentMethodCode "cash"
```

## Resultado esperado

```text
[PILOT-02] Protected metrics PASS
```
