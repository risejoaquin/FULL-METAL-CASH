# SolidPOS Iteration 21 — Hotfix 21.1

## Scope

Fixes the local production secret generator for Windows PowerShell environments where the static APIs below are not available:

- `[System.Security.Cryptography.RandomNumberGenerator]::Fill(...)`
- `[Convert]::ToHexString(...)`

## Change

`new-solidpos-secret.ps1` now uses:

- `RandomNumberGenerator.Create().GetBytes(...)`
- `BitConverter.ToString(...).Replace('-', '').ToLowerInvariant()`

This keeps cryptographic randomness while remaining compatible with Windows PowerShell / .NET Framework hosts.

## Validation

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind JwtSigningKey -Bytes 48
```

```powershell
.\scripts\security\new-solidpos-secret.ps1 -Kind ProvisionKey -Bytes 48
```

Expected: both commands generate secrets without `MethodNotFound`.
