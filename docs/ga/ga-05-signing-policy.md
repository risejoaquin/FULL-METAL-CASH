# GA-05 Signing Policy

## Required cryptographic evidence
Both `SolidPOS.PosCore.Wpf.exe` and the generated Velopack Setup.exe require an Authenticode signer certificate and `Get-AuthenticodeSignature` status `Valid` during validation.

## Production certificate mode
Preferred mode uses `-SigningCertificateThumbprint <thumbprint>` and resolves the certificate from `Cert:\CurrentUser\My`. The certificate must have a private key. Its subject and thumbprint are recorded in provenance; private-key material is never exported or logged.

## Validation-only fallback
When no thumbprint is supplied, GA-05 creates a temporary code-signing certificate and classifies it as:

`VALIDATION_SELF_SIGNED`

The public certificate is temporarily placed in CurrentUser TrustedPeople so Windows can validate the Authenticode chain during the test. The validator removes both certificate-store entries in `finally`.

This fallback proves signing mechanics but sets:

`productionSigningRequiredBeforePublicPromotion = True`

GA-06/public rollout must not treat `VALIDATION_SELF_SIGNED` as a production certificate.

## Invariants
- hash algorithm: SHA-256;
- signature must be present and valid;
- signer thumbprint must match the expected certificate;
- no certificate password or private key is persisted in repo or logs.


## Validation trust-chain behavior (HOTFIX GA-05.3)

For `VALIDATION_SELF_SIGNED` only, the validator temporarily imports the public certificate into `Cert:\CurrentUser\Root` and `Cert:\CurrentUser\TrustedPeople` so Windows Authenticode chain validation can return `Valid`. Both trust entries and the temporary private-key certificate are removed in `finally`. This temporary trust is validation evidence only and does not replace a production code-signing identity for public promotion.
