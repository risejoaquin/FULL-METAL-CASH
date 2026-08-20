# SolidPOS Iteration 21 — Production Security Closure

## Status

Prepared for validation. Do not mark PASS until production secrets are rotated and the security closure script passes against Railway.

## Goal

Close production security before pilot readiness by adding executable controls for:

- local secret scanning;
- secret generation without committing values;
- production health/readiness validation;
- admin login validation after rotation;
- protected observability validation;
- sync contract/runtime validation;
- provisioning status validation;
- refresh token revocation SQL;
- production security checklist.

## Added / Modified

```text
scripts/security/new-solidpos-secret.ps1
scripts/security/scan-local-secrets.ps1
scripts/security/validate-production-security-closure.ps1
scripts/security/revoke-production-refresh-tokens.sql
scripts/security/production-security-closure-checklist.md
ITERATION_21_VALIDATION_COMMANDS.md
SOLIDPOS_ITERATION_21_PRODUCTION_SECURITY_CLOSURE.md
```

## Rotation policy

The repository does not contain production secrets. Secret rotation must happen in Railway/Supabase.

Required rotation:

1. Supabase/PostgreSQL password.
2. Railway `DATABASE_URL` or `ConnectionStrings__Postgres`.
3. `Jwt__SigningKey`.
4. `Provisioning__BootstrapKey` / `PROVISION_KEY`.
5. Revoke active refresh tokens.

## Validation command

```powershell
$securePassword = Read-Host -AsSecureString "Production admin password"

.\scripts\security\validate-production-security-closure.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword
```

## Expected closure result

```text
SolidPOS production security closure validation completed.
```

## Architectural decision

Iteration 21 does not add product features. It establishes the security gate required before pilot readiness. The gate is intentionally operational and repeatable: it can be rerun after every secret rotation or deployment.
