# EXP-10 Admin Management Rollback

## Rollback strategy
EXP-10 does not delete production evidence. Rollback is operational and non-destructive.

## Controlled user rollback
If the EXP-10 support user must be disabled, update status to `suspended` through `/api/v1/users/{userId}`. Do not hard-delete the user.

## Controlled customer rollback
If the EXP-10 customer must be removed from active lists, update status to `archived` through `/api/v1/customers/{customerId}`. Do not hard-delete the customer.

## Audit
Rollback must preserve audit trail. User/customer rollback actions must remain traceable.

## NO-GO
Manual SQL deletion is not part of this rollback policy.
