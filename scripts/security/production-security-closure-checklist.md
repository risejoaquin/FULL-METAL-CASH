# SolidPOS Iteration 21 — Production Security Closure Checklist

Use this checklist before declaring the production pilot ready.

## 1. Rotate exposed or test secrets

Rotate these values outside Git and outside chat:

1. Supabase/PostgreSQL database password.
2. Railway `DATABASE_URL` or `ConnectionStrings__Postgres`.
3. `Jwt__SigningKey` with a new 32+ byte value.
4. `Provisioning__BootstrapKey` or `PROVISION_KEY`.
5. Any local `.env` values copied during testing.

Generate replacement candidates locally:

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind JwtSigningKey -Bytes 48
.\scripts\security\new-solidpos-secret.ps1 -Kind ProvisionKey -Bytes 48
```

## 2. Revoke active refresh tokens

After Railway is updated and the admin can log in, revoke previous refresh tokens:

```sql
-- scripts/security/revoke-production-refresh-tokens.sql
UPDATE pos.refresh_tokens
SET revoked_at = now(), revoked_reason = 'production_security_closure_rotation'
WHERE revoked_at IS NULL;
```

## 3. Keep demo credentials disabled in production

The demo account `owner@solidpos.local` must not be used for production operations. Keep the production tenant admin as the only pilot admin unless more users are intentionally provisioned.

## 4. Validate production closure

Use a secure prompt for the admin password instead of typing it into shell history:

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"

.\scripts\security\validate-production-security-closure.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword
```

Expected final message:

```text
SolidPOS production security closure validation completed.
```

## 5. Do not commit generated artifacts

The root `.gitignore` must keep these out of Git:

- `.env`, `.env.*`
- `.runtime/`
- `*.sqlite`, `*.sqlite-wal`, `*.sqlite-shm`
- `node_modules/`, `dist/`
- `*.pkg`, `*.setup.exe`, `*.zip`
- `*.log`, dumps and diagnostics
