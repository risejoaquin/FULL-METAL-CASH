# SolidPOS Iteration 18 — PosBuilder + Updates Real Packaging

## Objective

Move PosBuilder from tenant branding package generation to local update packaging foundation:

- deterministic local package artifact
- update manifest JSON
- SHA-256 checksum
- package size verification
- stable/dev channel metadata
- release version metadata
- minimum PosCore/PosBuilder compatibility metadata
- PosBuilder self-test without blocking WPF UI
- PosCore WPF consumption of branding + update manifest

## Scope

This iteration does not integrate Velopack binaries yet. It creates the packaging contract and validation boundary required before Velopack packaging is introduced.

## New domain contract

`UpdatePackageManifest`:

- `tenantId`
- `tenantName`
- `appName`
- `releaseVersion`
- `channel`
- `packageKind`
- `packageFileName`
- `packageSizeBytes`
- `sha256`
- `minimumPosCoreVersion`
- `minimumPosBuilderVersion`
- `brandingPackageVersion`
- `generatedAtUtc`
- `notes`

## New CLI commands

```text
create-update-package
validate-update-package
show-update-package
```

## Validation

Run `ITERATION_18_VALIDATION_COMMANDS.md`.

## Architectural decision

Updates are represented by a manifest + immutable package file. PosBuilder owns generation and validation. PosCore/WPF consumes the manifest but does not build packages.
