# POST-LGA Capacity Hotfix 01 — Validation Commands

Use the same POST-LGA validator invocation already documented. Confirm `curl.exe` first:

```powershell
Get-Command curl.exe
```

Then execute `scripts\ga\validate-post-lga-capacity-infrastructure-remediation.ps1` with the existing strict parameters:
- concurrency 3
- requests 6
- max p95 1200 ms
- waiting connections max 12
- negative stock 0
- PublicGaDecision KEEP_LIMITED_GA
