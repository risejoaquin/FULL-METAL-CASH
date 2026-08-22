# GA-05 Provenance and Artifact Retention

## Source reference
When Git metadata is unavailable in the distributed ZIP, the canonical source reference is a deterministic repository SHA-256 computed while excluding `.git`, `.runtime`, `bin`, `obj`, `node_modules`, `dist`, and `TestResults`.

The provenance also records the GA-04 source ZIP SHA-256:

`33313dfc7c85cc855286e26e5d263eeece171ead9c6dbbfb300bc4d6663f37e6`

If `.git` is present, the current commit hash is included additionally; it does not replace the repository SHA-256.

## Build provenance
The evidence records:
- Release configuration;
- win-x64 runtime;
- self-contained publish;
- deterministic build flag;
- two independent publish hashes;
- Velopack 1.2.0 tool/package version;
- installer SHA-256;
- every Velopack output artifact hash;
- signer certificate subject and thumbprint;
- rollback version;
- schema/update contract compatibility.

## Dependency inventory
GA-05 executes `dotnet list <solution> package --include-transitive --format json` and retains the resulting dependency inventory. This is a dependency/SBOM evidence input, not a claim that a full SPDX or CycloneDX SBOM generator exists in the current repository.

## Retention
Default retention is **180 days**. `ga-05-artifact-retention.json` records `retainUntilUtc`, hashes and artifact names. Automatic deletion is false.
