# HOTFIX GA-07.1 Validation

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos
Unblock-File .\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1
.\scripts\ga\validate-ga-07-backup-restore-rollback-disaster-recovery.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

Expected next checkpoint:
`[GA-07] GA-07 production source snapshot / rollback artifact gate PASS`

Required final result:
`[GA-07] GA-07 PASS GA BACKUP RESTORE ROLLBACK DISASTER RECOVERY / GO GA-08`
