# SolidPOS BETA-02 — Beta Tenant Provisioning and Separation Hardening

## Changes
1. Hardened production bootstrap idempotency by validating the persisted request hash before replay.
2. Expanded the non-secret provisioning fingerprint so semantically changed payloads cannot reuse an old idempotency result.
3. Added PostgreSQL regression coverage for changed-payload reuse.
4. Added BETA-02 PowerShell production validator.
5. Added BETA-02 SQL source-of-truth validator.
6. Added provisioning, separation, negative-test and GO/NO-GO documentation.

## Modules affected
- PosServer Infrastructure / Provisioning.
- PosServer IntegrationTests.
- scripts/beta.
- docs/beta.

## Technical decision
Cross-tenant production tests are read-only. BETA-02 does not deliberately mutate a foreign tenant to prove isolation. API list ownership is cross-checked against SQL and foreign direct reads must return 404.

## Risk
The provisioning repository code changed and must pass the user's real .NET build/test gate. This environment does not contain the .NET SDK or PowerShell, so no build/test/production PASS is claimed here.

## State
`PASS BETA TENANT PROVISIONING SEPARATION HARDENING / GO BETA-03`
