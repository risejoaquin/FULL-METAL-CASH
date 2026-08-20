# SolidPOS Iteration 22 — Validation Commands

## 1. Backend restore

```powershell
dotnet restore solidpos-platform.sln
```

Expected: restore completed without errors.

## 2. Backend build

```powershell
dotnet build solidpos-platform.sln
```

Expected:

```text
Compilación correcta.
0 Advertencia(s)
0 Errores
```

## 3. Backend tests

```powershell
dotnet test solidpos-platform.sln
```

Expected:

```text
SolidPOS.PosCore.UnitTests       PASS
SolidPOS.PosServer.UnitTests     PASS
SolidPOS.PosServer.IntegrationTests PASS
SolidPOS.PosServer.ContractTests PASS
```

## 4. Dashboard operations validation

```powershell
.\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

Expected:

```text
vite build
built in ...
Audit client ready: /api/v1/audit/events.
message : PosDashboard reports audit operations dashboard completed.
```

## 5. Production security validation

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"
```

```powershell
.\scripts\security\validate-production-security-closure.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword
```

Expected:

```text
[SECURITY] Production readiness PASS
[SECURITY] Admin login with rotated credentials PASS
[SECURITY] Protected metrics PASS
[SECURITY] Sync contract schema PASS
message : SolidPOS production security closure validation completed.
```

## 6. Confirm refresh token revocation in Supabase

```sql
SELECT count(*) AS active_refresh_tokens
FROM pos.refresh_tokens
WHERE revoked_at IS NULL;
```

Expected:

```text
active_refresh_tokens = 0
```

## 7. Production pilot readiness validation

```powershell
.\scripts\pilot\validate-production-pilot-readiness.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword
```

Expected:

```text
[PILOT] Local repository guardrails PASS
[PILOT] Local secret scan PASS
[PILOT] PosDashboard production build and self-test PASS
[PILOT] Production liveness PASS
[PILOT] Production readiness PASS
[PILOT] Admin login PASS
[PILOT] Protected metrics PASS
[PILOT] Sync contract schema 4 PASS
[PILOT] Sync runtime status PASS
[PILOT] Provisioning status endpoint PASS
[PILOT] Sales read model PASS
[PILOT] Returns read model PASS
[PILOT] Audit events read model PASS
message : SolidPOS production pilot readiness validation completed.
```

## Logs to send if failure

Send the complete block starting at the first failed step. Do not continue dependent steps after a failed build, failed dashboard build, failed readiness, failed login, or failed pilot validation.
