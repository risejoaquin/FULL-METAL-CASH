# HOTFIX GA-05.3 — Validation Self-Signed Trust Chain

## Trigger
GA-05.2 successfully reached Authenticode signing with a `VALIDATION_SELF_SIGNED` certificate, but `Get-AuthenticodeSignature` returned `UnknownError` because the self-signed certificate terminated in a root that was not trusted by the current user.

## Root cause
Importing the temporary validation certificate only into `Cert:\CurrentUser\TrustedPeople` identifies the signer but does not establish the certificate as a trusted root for Windows chain validation. Because the validation certificate is self-signed, its own public certificate must be trusted temporarily as a root for the Authenticode validation window.

## Fix
When and only when `signingIdentityType = VALIDATION_SELF_SIGNED`:

1. create the short-lived code-signing certificate in `Cert:\CurrentUser\My`;
2. export the public certificate;
3. import it temporarily into `Cert:\CurrentUser\Root`;
4. retain the existing temporary `TrustedPeople` entry;
5. sign and require `Get-AuthenticodeSignature.Status = Valid`;
6. remove both temporary trust entries in `finally`;
7. remove the temporary private-key certificate from `CurrentUser\My`.

A user-provided production certificate is not inserted into any trust store by this hotfix.

## Invariants
- No stable release is promoted.
- `generalAvailabilityActivated = False`.
- `schemaVersion = 4`.
- `syncContract = schema_version_4`.
- Production signing remains required before public promotion when the GA-05 RC used `VALIDATION_SELF_SIGNED`.
- The validation trust anchor is temporary and removed even when the validator fails after creation.
