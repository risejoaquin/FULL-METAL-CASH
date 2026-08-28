# GA-05 Stable Release Candidate Build, Signing and Provenance

GA-05 converts the validated production baseline into a concrete **stable release candidate** that remains **not promoted** to the stable channel.

## Entry gate
Fresh GA-04 must return `PASS GA PRODUCTION DATA INTEGRITY FINANCIAL RECONCILIATION / GO GA-05`, blockers must be empty, schema version must remain 4 and General Availability must remain inactive.

## Candidate requirements
- target channel: `stable`, candidate only;
- package: real Velopack Setup.exe;
- Windows runtime: win-x64;
- release build: Release + deterministic CI settings;
- package and CLI: Velopack 1.2.0;
- rollback: latest active valid beta release version;
- mandatory: false;
- tenant scoped: true;
- schema version 4;
- update contract remains compatible with `/api/v1/updates/channels` and the current builder update release contract;
- stable production release count must remain zero throughout GA-05.

## Release candidate output
The validator writes local candidate metadata, dependency inventory, provenance, hashes, Authenticode evidence, retention policy, release notes and the signed installer under the GA-05 runtime directory.

## No stable promotion
A successful GA-05 means the RC is technically buildable and traceable. GA-06 owns stable channel promotion and cohort update dry-run behavior.
