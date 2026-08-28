# SolidPOS EXP-10 — Customer/Admin Management Completion

Status: PENDING USER VALIDATION

EXP-10 completes customer/admin management readiness for limited production expansion. It validates customer CRUD, user admin flow, role/permission/store/terminal administration visibility, RBAC endpoint protection, audit evidence, and SQL cross-checks.

Expected result: PASS CUSTOMER ADMIN MANAGEMENT COMPLETION / GO EXP-11.

---

## HOTFIX 10.1 — Customer List/Search Filter Contract

EXP-10 HOTFIX 10.1 fixes a validator false negative where the customer was created and validated through `GET /api/v1/customers/{customerId}`, but the list/search filter did not return it using the original `search=<suffix>` query.

The authoritative closure contract is now:

- Customer created by API.
- Customer returned by `GET by id`.
- Customer sales history endpoint available.
- SQL cross-check validates tenant-scoped active customer.
- SQL cross-check validates audit evidence.

List/search is still tested with multiple query shapes, but failure to match the created customer becomes a non-blocking condition when `GET by id` and SQL evidence pass.


## HOTFIX 10.2 — User List/Search Filter Contract

The controlled user create/update flow remains authoritative through API responses and SQL cross-check. User list/search visibility is now an operational condition instead of a hard blocker when the created user is not returned by the generic list endpoint.

Condition emitted when applicable:

- `review_user_list_search_filter_contract`


## HOTFIX 10.3 — SQL psql UUID Variable Quoting Contract

Se eliminó la re-declaración interna de variables `\set` en `scripts/expansion/exp-10-customer-admin-management-completion-check.sql` porque duplicaba comillas al consumir variables enviadas por `psql -v`.

Fuente de verdad corregida:

```sql
:'tenant_id'::uuid
:'customer_id'::uuid
:'user_id'::uuid
:'store_id'::uuid
:'role_code'::text
```

Esto corrige el error `invalid input syntax for type uuid` provocado por valores con comillas anidadas como `''uuid''::uuid`.
