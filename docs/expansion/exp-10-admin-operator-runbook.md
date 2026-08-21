# EXP-10 Admin Operator Runbook

## Daily admin operations
- Review tenant status before changes.
- List stores and confirm active store coverage.
- List users and confirm no unexpected suspended/unknown admin accounts.
- Review roles and permissions before assigning access.
- Use store-scoped access for operational users.
- Use terminal list to confirm active devices.

## User administration
Create users through `/api/v1/users` with explicit role codes and store ids. Update users through `/api/v1/users/{userId}`. Do not assign global access when store-scoped access is enough.

## Customer administration
Create customers through `/api/v1/customers`, update status/phone/credit limit through `/api/v1/customers/{customerId}`, and inspect history through `/api/v1/customers/{customerId}/sales`.

## Audit trail
Every user or customer mutation must have audit evidence. Missing audit evidence is a NO-GO for admin operations.
