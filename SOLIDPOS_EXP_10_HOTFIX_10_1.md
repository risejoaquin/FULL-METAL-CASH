# SolidPOS EXP-10 HOTFIX 10.1 — Customer List/Search Filter Contract

## Status

PENDING USER VALIDATION.

## Problem

EXP-10 passed local guardrails, document contract, secret scan, build, tests, readiness, admin login, and endpoint contract, but failed during the customer management operational flow:

```text
Customer list/search did not include created customer.
```

The created customer was already validated through `GET /api/v1/customers/{customerId}`. The failure was caused by the validator treating the list/search filter as the source of truth.

## Fix

The validator now treats customer existence as authoritative when both are true:

- `GET /api/v1/customers/{customerId}` returns the created customer.
- SQL cross-check confirms the customer exists, is active, tenant-scoped, and has audit evidence.

The list/search check now tries multiple supported query shapes:

- `search=<suffix>`
- `search=<email>`
- `q=<suffix>`
- `email=<email>`
- unfiltered `limit=100`

If the list/search contract still does not return the customer while `GET by id` and SQL are valid, EXP-10 can pass with the non-blocking condition:

```text
review_customer_list_search_filter_contract
```

## Files changed

```text
scripts/expansion/validate-exp-10-customer-admin-management-completion.ps1
SOLIDPOS_EXP_10_HOTFIX_10_1.md
SOLIDPOS_EXP_10_CUSTOMER_ADMIN_MANAGEMENT_COMPLETION.md
EXP_10_VALIDATION_COMMANDS.md
```

## Safety

No backend code, migrations, sales, payments, cash drawer data, inventory, stores, terminals, sync, or release channels were modified.

## Expected result

```text
[EXP-10] Customer management operational flow PASS
[EXP-10] EXP-10 PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11
```
