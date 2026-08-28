# SolidPOS Production Secret Rotation Checklist

Use after any accidental secret exposure, environment copy, team handoff, or production incident.

## Required rotation order

1. Rotate PostgreSQL/Supabase database password.
2. Update Railway variable `DATABASE_URL` or `ConnectionStrings__Postgres`.
3. Redeploy PosServer and verify `/health/ready`.
4. Rotate `Jwt__SigningKey` to a new 32+ byte random value.
5. Redeploy PosServer; all existing access tokens become invalid.
6. Revoke active refresh tokens if credential exposure is suspected:

```sql
UPDATE pos.refresh_tokens
SET revoked_at = now(), revoked_reason = 'security_rotation'
WHERE revoked_at IS NULL;
```

7. Replace the demo `owner@solidpos.local` account with a real production admin account.
8. Suspend or delete the demo user in production after a real admin is validated.
9. Run the deployment smoke test.

## Validation

```powershell
.\scripts\smoke-test-deployment.ps1 -BaseUrl "https://TU-SERVICIO.up.railway.app"
```

## Do not do this in production

- Do not keep `Admin123!` as a valid password.
- Do not paste secrets in chat, GitHub issues, logs, screenshots, or markdown files.
- Do not commit `.env`, `appsettings.Production.json`, connection strings, JWT keys, API keys, or database passwords.
