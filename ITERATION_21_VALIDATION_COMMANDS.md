# SolidPOS Iteration 21 — Validation Commands

## 1. Backend solution validation

```powershell
dotnet restore solidpos-platform.sln
```

```powershell
dotnet build solidpos-platform.sln
```

```powershell
dotnet test solidpos-platform.sln
```

## 2. Dashboard production build validation

```powershell
.\scripts\posdashboard\validate-posdashboard-operations-dashboard.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com"
```

## 3. Generate replacement secret candidates

Do not paste generated values in chat or commit them.

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind JwtSigningKey -Bytes 48
```

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind ProvisionKey -Bytes 48
```

## 4. Rotate production secrets outside Git

Rotate in Supabase/Railway:

```text
Supabase database password
Railway DATABASE_URL / ConnectionStrings__Postgres
Jwt__SigningKey
Provisioning__BootstrapKey / PROVISION_KEY
```

Redeploy Railway after updating variables.

## 5. Revoke old refresh tokens

Run after the new admin login is confirmed:

```sql
-- scripts/security/revoke-production-refresh-tokens.sql
UPDATE pos.refresh_tokens
SET revoked_at = now(), revoked_reason = 'production_security_closure_rotation'
WHERE revoked_at IS NULL;
```

## 6. Production security closure validation

Use a secure password prompt:

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
[SECURITY] Local .gitignore guardrails PASS
[SECURITY] Local secret scan PASS
[SECURITY] Production liveness PASS
[SECURITY] Production readiness PASS
[SECURITY] Admin login with rotated credentials PASS
[SECURITY] Protected metrics PASS
[SECURITY] Sync contract schema PASS
[SECURITY] Sync runtime status PASS
[SECURITY] Provisioning status endpoint PASS
message : SolidPOS production security closure validation completed.
```

## Hotfix 21.1 validation

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind JwtSigningKey -Bytes 48
```

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind ProvisionKey -Bytes 48
```

Expected: both commands generate a value and do not fail with `RandomNumberGenerator.Fill` or `Convert.ToHexString` method errors.
