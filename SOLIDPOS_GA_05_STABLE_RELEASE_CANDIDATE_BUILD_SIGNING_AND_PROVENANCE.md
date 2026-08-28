# SolidPOS GA-05 — Stable Release Candidate Build, Signing and Provenance

Status: `PASS REAL PRODUCTION`

GA-05 creates a real Windows stable-channel **release candidate only**. It does not create or promote a `stable` release row in production and it does not activate General Availability.

## Required candidate contract
- `packageType = velopack`
- `Velopack 1.2.0` package and CLI are pinned together.
- `signaturePresent = True`
- `signatureValid = True`
- `universalInstaller = True`
- `rollbackVersion != null`
- `mandatory = False`
- `tenantScoped = True`
- `schemaVersion = 4`
- `syncContract = schema_version_4`
- `generalAvailabilityActivated = False`
- stable promotion is not performed in GA-05.

## Build reproducibility
The validator performs two independent Release `dotnet publish` operations for `win-x64`, self-contained, with deterministic/CI build settings. It fingerprints the complete publish payload excluding PDB files and requires both fingerprints to match before packaging.

## Real Velopack packaging
`SolidPOS.PosCore.Wpf` references `Velopack 1.2.0` and initializes `VelopackApp.Build().Run()` before normal WPF startup. The validator pins the `vpk` CLI to 1.2.0 and executes `vpk pack` to produce a real Windows Setup.exe and Velopack release package metadata.

## Authenticode signing
GA-05 does not accept a random string as signing evidence. The main PosCore executable and the Velopack Setup.exe are signed and verified with Windows Authenticode.

Two signing modes exist:
1. a certificate supplied through `-SigningCertificateThumbprint` from `Cert:\CurrentUser\My`; or
2. a short-lived `VALIDATION_SELF_SIGNED` code-signing certificate generated only for the GA-05 validation run and removed from the certificate stores afterward.

The second mode proves the cryptographic build/sign/verify pipeline but is explicitly marked `productionSigningRequiredBeforePublicPromotion = True`. GA-06 must not reinterpret a validation-only identity as a public production signing identity.

## Provenance
The candidate records:
- release version;
- rollback version;
- deterministic publish fingerprint;
- installer SHA-256;
- Authenticode signer subject and thumbprint;
- source repository SHA-256 reference;
- source GA-04 ZIP SHA-256;
- optional Git commit when `.git` is available;
- pinned Velopack library/CLI version;
- .NET dependency inventory;
- release file hashes;
- retention deadline.

## Artifact retention
GA-05 retains the generated candidate evidence for at least 180 days by default. Runtime artifacts are generated under `.runtime/ga-05-stable-release-candidate-build-signing-provenance/` and are never automatically deleted by the validator.

## Safety boundary
GA-05 is allowed to build local artifacts and use the current-user certificate store temporarily for validation signing. It must not:
- create or promote a stable production release;
- set `mandatory = True`;
- activate GA;
- change schema version;
- rewrite commercial data;
- delete release history.

## Required result
`PASS GA STABLE RELEASE CANDIDATE BUILD SIGNING PROVENANCE / GO GA-06`


## HOTFIX GA-05.2 — CLI version verification
The pinned Velopack CLI version is verified from the isolated `.NET tool` installation manifest/listing rather than by assuming a root-level `vpk --version` command. The required package remains exactly `vpk 1.2.0`.

## HOTFIX GA-05.4

GA-05.4 moves the Velopack lifecycle hook to the explicit WPF `Program.Main()` entry point and changes cleanup of GA-05-created validation certificates to a non-interactive `certutil -user -delstore` path with residual-store verification. Production certificates supplied by thumbprint remain untouched.


## Production closure
GA-05 was validated in real production on 2026-08-22 with artifact SHA-256 `eaab04c0c3a72611d2a27a46e97451d2d036b23d36a2e20d303caf3b184bb274`. GA-06 is authorized.
