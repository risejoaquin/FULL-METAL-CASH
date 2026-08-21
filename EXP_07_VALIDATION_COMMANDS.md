# SolidPOS EXP-07 Validation Commands

## Phase

SolidPOS EXP-07 — Sync SLA and Offline Reliability Hardening

## Status

PENDING USER VALIDATION

## Main command

```powershell
cd C:\Users\Lucilfer\Documents\SolidPos

$securePassword = Read-Host -AsSecureString "Production admin password"
$env:DATABASE_URL = Read-Host "DATABASE_URL Supabase"
$env:DATABASE_URL.Substring(0,13)

Unblock-File .\scripts\expansion\validate-exp-07-sync-sla-offline-reliability-hardening.ps1
Unblock-File .\scripts\security\scan-local-secrets.ps1

.\scripts\expansion\validate-exp-07-sync-sla-offline-reliability-hardening.ps1 `
  -BaseUrl "https://full-metal-cash-production.up.railway.app" `
  -TenantId "0ce5bbd0-528b-4aee-9fe3-93df001a4fde" `
  -Email "admin@micafeteria.com" `
  -Password $securePassword `
  -DatabaseUrl $env:DATABASE_URL `
  -SkipDashboardBuild
```

## Expected result

```text
[EXP-07] EXP-07 PASS SYNC SLA AND OFFLINE RELIABILITY HARDENING / GO EXP-08
```

## Expected artifacts

```text
docs/expansion/logs/exp-07-sync-sla-offline-reliability-hardening-log.md
.runtime/exp-07-sync-sla-offline-reliability-hardening/sync-sla-offline-reliability-manifest.json
```

## If it fails

Send:

```text
PowerShell output from first [EXP-07]
docs/expansion/logs/exp-07-sync-sla-offline-reliability-hardening-log.md if present
.runtime/exp-07-sync-sla-offline-reliability-hardening/sync-sla-offline-reliability-manifest.json if present
scripts/expansion/validate-exp-07-sync-sla-offline-reliability-hardening.ps1
scripts/expansion/exp-07-sync-sla-offline-reliability-hardening-check.sql
```
