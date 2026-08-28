# EXP-10 Customer Management Runbook

## Customer lifecycle
- Create active customer.
- List by search query.
- Read by id.
- Update phone, credit limit, and status.
- Read sales history even when empty.

## Validation
A customer is operationally valid when it has tenant id, name, status, created/updated timestamps, and can be found through list and get endpoints.

## Audit trail
Customer creation and customer update must emit `customer.created` and `customer.updated` audit events.

## Safety
EXP-10 controlled customers are test evidence. They do not create sales, payments, returns, refunds, inventory movements, or cash drawer movements.
