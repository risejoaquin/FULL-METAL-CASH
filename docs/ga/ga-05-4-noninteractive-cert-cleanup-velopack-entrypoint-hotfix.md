# HOTFIX GA-05.4 — Non-interactive validation certificate cleanup and Velopack entry point

## Trigger

The GA-05.3 production validation proved that the temporary validation trust chain works and that the published PosCore executable receives a valid Authenticode signature. Velopack 1.2.0 also built `SolidPOS.PosCore-stable-Setup.exe` successfully.

The validator then failed in `finally` while removing the temporary certificate from `Cert:\CurrentUser\Root`. The PowerShell certificate provider caused an interactive Windows root-certificate deletion confirmation and the operation was cancelled. Velopack also reported that `VelopackApp.Build().Run()` was located in `App::.ctor()` instead of the executable entry point.

## Root causes

1. Cleanup of a temporary trusted root used the interactive PowerShell certificate provider deletion path.
2. The Velopack lifecycle hook was executed early in WPF startup, but not from the real PE entry point that Velopack expects.

## Changes

- Added explicit `SolidPOS.PosCore.Wpf.Program.Main()` and configured it as `<StartupObject>`.
- `VelopackApp.Build().Run()` is the first application lifecycle action in `Program.Main()`.
- Removed the Velopack lifecycle call from the `App` constructor.
- Temporary `VALIDATION_SELF_SIGNED` certificate cleanup now uses `certutil.exe -user -delstore` for `TrustedPeople`, `Root`, and `My`.
- Cleanup verifies that no certificate with the validation thumbprint remains in any of those three user stores.
- Cleanup remains scoped exclusively to certificates created by GA-05; externally supplied production-signing certificates are never deleted.

## Safety properties

- No database writes.
- No release promotion.
- No stable-channel activation.
- No General Availability activation.
- No weakening of Authenticode verification.
- Temporary trust residue is treated as a hard cleanup failure.

## Revalidation

Run the normal GA-05 validator with `ReleaseVersion=1.0.0-rc.1`. Do not mark GA-05 PASS unless the validator ends with:

`[GA-05] GA-05 PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06`
