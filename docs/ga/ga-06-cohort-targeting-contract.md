# GA-06 Cohort Targeting Contract

## Request contract
`CreateUpdateReleaseRequest` accepts optional `targetTerminalIds` when `tenantScoped = true`.

## Check contract
`GET /api/v1/updates/check` accepts optional `terminalId`.

For a release with one or more target rows:
- matching active terminal -> release may be returned;
- terminal outside cohort -> `updateAvailable = false`;
- missing `terminalId` -> targeted release is not returned.

For a release with zero target rows, prior tenant-wide behavior is preserved. This targeting extension is backward compatible with existing tenant-wide releases.

GA-06 selects exactly one controlled validation terminal with a non-empty app version and requires at least one active terminal outside cohort for the negative test. No rollout outside cohort is acceptable.
