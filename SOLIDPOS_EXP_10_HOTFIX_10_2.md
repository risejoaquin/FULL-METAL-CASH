# SolidPOS EXP-10 HOTFIX 10.2 — User List/Search Filter Contract

## Status

PENDING USER VALIDATION

## Reason

EXP-10 passed repository, docs, secret scan, restore, build, tests, health/readiness, admin endpoint contract, and customer management flow. It failed during controlled user management because the created user was not returned by the generic users list endpoint.

## Diagnosis

The created user was already validated through:

- POST `/api/v1/users`
- PATCH `/api/v1/users/{userId}`
- role assignment response
- store access assignment response

The blocking assertion was too strict because list/search pagination or filter behavior may not include the newly created user in `/api/v1/users`.

## Fix

The validator now probes multiple user list/search shapes:

- `search=<suffix>`
- `search=<email>`
- `q=<suffix>`
- `email=<email>`
- `limit=100`
- plain `/api/v1/users`

If list/search does not return the user, EXP-10 can still close when the authoritative checks pass:

- create response
- update response
- SQL cross-check in `pos.users`
- SQL cross-check in `pos.user_roles`
- SQL cross-check in `pos.user_store_access`
- SQL audit evidence

The condition recorded is:

- `review_user_list_search_filter_contract`

## Files changed

- `scripts/expansion/validate-exp-10-customer-admin-management-completion.ps1`
- `SOLIDPOS_EXP_10_HOTFIX_10_2.md`
- `SOLIDPOS_EXP_10_CUSTOMER_ADMIN_MANAGEMENT_COMPLETION.md`
- `EXP_10_VALIDATION_COMMANDS.md`

## Safety

No backend, migrations, production data model, sales, payments, cash, inventory, sync, stores, terminals, release channels, or tenant identity were changed.
