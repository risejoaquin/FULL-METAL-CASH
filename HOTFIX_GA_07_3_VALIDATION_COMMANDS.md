# HOTFIX GA-07.3 — Restore Extension Bootstrap Verification

## Cause
GA-07.2 reached the isolated restore and `pg_restore` failed because restored tables reference `public.citext` while the temporary database did not have the required extension visible at restore time.

## Fix
- Make restore helper argument binding explicit with `-Name`, `-PsqlArguments`, and `-Output`.
- Bootstrap `citext` and `pgcrypto` in the isolated restore database before `pg_restore`.
- Query `pg_extension` immediately and fail closed unless both extensions are present.
- No production schema, data, API, or deployment change.

## Execute
```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
Unblock-File .\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Expected checkpoint
`[GA-07] Restore prerequisite extensions PASS`

Final required:
`[GA-07] GA-07 PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08`
