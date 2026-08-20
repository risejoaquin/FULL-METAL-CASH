# SolidPOS PILOT-02 — Validation Commands

## 1. Restaurar solución

```powershell
dotnet restore solidpos-platform.sln
```

## 2. Compilar solución

```powershell
dotnet build solidpos-platform.sln
```

Esperado:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## 3. Ejecutar pruebas

```powershell
dotnet test solidpos-platform.sln
```

Esperado:

```text
SolidPOS.PosCore.UnitTests            PASS
SolidPOS.PosServer.UnitTests          PASS
SolidPOS.PosServer.IntegrationTests   PASS
SolidPOS.PosServer.ContractTests      PASS
```

## 4. Cargar DATABASE_URL de Supabase

```powershell
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
```

Validar prefijo:

```powershell
$env:DATABASE_URL.Substring(0,13)
```

Esperado:

```text
postgresql://
```

## 5. Cargar contraseña admin

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
```

## 6. Ejecutar PILOT-02

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
[PILOT-02] Local repository guardrails PASS
[PILOT-02] Local secret scan PASS
[PILOT-02] PosDashboard production build and self-test PASS
[PILOT-02] Production liveness PASS
[PILOT-02] Production readiness PASS
[PILOT-02] Admin login PASS
[PILOT-02] Protected metrics PASS
[PILOT-02] Production pilot data lookup via PostgreSQL PASS
[PILOT-02] Terminal enrollment/register PASS
[PILOT-02] Closing stale open PILOT-02 cash shifts PASS
[PILOT-02] Opening cash shift PASS
[PILOT-02] Creating real controlled POS sale PASS
[PILOT-02] Validating sale detail and read model PASS
[PILOT-02] Issuing and validating digital receipt PASS
[PILOT-02] Validating shift summary and closing shift PASS
[PILOT-02] Validating audit event read model PASS
[PILOT-02] Validating transaction persistence via PostgreSQL PASS
[PILOT-02] Pilot transaction log initialized PASS

goNoGo : GO
message : SolidPOS PILOT-02 real POS transaction validation completed.
```

## Si falla

Enviar:

- bloque completo de PowerShell;
- último `[PILOT-02]` exitoso;
- primer error después del último PASS;
- si falla SQL, enviar tabla de salida de `pilot-02-transaction-check.sql`.


## Hotfix 02.1

Corrige compatibilidad con la forma real del endpoint `/api/v1/observability/metrics`: `metrics.database.ready` y `metrics.database.requiredTablesPresent`.


## Hotfix 02.2

If the SQL validation fails with `relation "pos.sale_payments" does not exist`, apply Hotfix 02.2 and rerun only:

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


## Hotfix 02.3

If local secret scan prints `No obvious secret patterns found` but the pilot script still throws `Local secret scan failed`, apply Hotfix 02.3 and rerun only `validate-real-pos-transaction.ps1`.


## Hotfix 02.6

Reejecutar solo el validador PILOT-02 después de aplicar el ZIP. No repetir restore/build/test si no hubo cambios de código .NET.
