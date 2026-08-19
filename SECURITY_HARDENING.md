# SolidPOS Security Hardening

## Production requirements

- Use Railway/Supabase secrets only through environment variables.
- Never commit `.env`, production appsettings, database URLs, JWT signing keys, or passwords.
- Rotate the PostgreSQL password and `Jwt__SigningKey` after exposure or handoff.
- Replace demo credentials before production use.
- Disable `owner@solidpos.local` after a real production admin has been validated.
- Keep `ENABLE_SWAGGER=false` in production unless explicitly needed for controlled diagnostics.

## Authentication controls

- JWT access token lifetime is short.
- Refresh tokens are stored hashed.
- Refresh token rotation rejects reuse/race attempts.
- Account lockout mitigates brute force attempts.
- Password policy blocks weak/common/demo passwords for new admin-managed users.

## Required production cleanup

After creating a real admin user, run:

```sql
\i scripts/security/disable-demo-user.sql
```

Then smoke test the deployment with the real admin path or with a controlled staging-only account.

## Secret scanning

Local:

```powershell
.\scripts\security\scan-local-secrets.ps1
```

GitHub:

- Dependabot enabled for NuGet and GitHub Actions.
- Security scan workflow available manually and weekly.
