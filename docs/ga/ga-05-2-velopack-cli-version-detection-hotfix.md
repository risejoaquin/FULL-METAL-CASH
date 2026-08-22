# HOTFIX GA-05.2 — Velopack CLI version detection

## Status
PENDING USER VALIDATION

## Failure corrected
GA-05.1 successfully restored and published `win-x64` twice with an identical payload hash, then failed while validating the installed Velopack CLI because `vpk 1.2.0` treats a root-level `--version` invocation as a missing command and returns `Required command was not provided.`

## Technical decision
The validator no longer assumes that `vpk --version` is a supported CLI contract. The Velopack executable is still pinned and installed with `dotnet tool install --version 1.2.0`; its installed package version is now independently verified through `dotnet tool list --tool-path <runtime tools dir>`, which is the .NET tool-management source of truth for that isolated tool path.

The validator requires an exact `vpk` entry at version `1.2.0` before packaging may continue.

## Scope
- `scripts/ga/validate-ga-05-stable-release-candidate-build-signing-provenance.ps1`
- GA-05 validation documentation only.

No schema, database, API, inventory, sync, release-channel, signing-policy, or production-state mutation is introduced.

## Expected continuation
After this hotfix the validator should continue from Velopack acquisition into `vpk pack`, Authenticode signing/verification, checksum/provenance, and the final GA-05 gate.
