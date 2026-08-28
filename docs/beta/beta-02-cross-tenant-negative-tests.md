# BETA-02 Cross-Tenant Negative Tests

BETA-02 intentionally uses read-only probes in production. The validator obtains sampled foreign ids through the SQL source-of-truth and verifies that an authenticated admin from the target tenant cannot read them.

Required behavior:
- Foreign customer GET -> 404 when a foreign customer fixture exists.
- Foreign sale GET -> 404 when a foreign sale fixture exists.
- Store list contains only target-tenant store ids.
- User list contains only target-tenant user ids.
- Terminal list contains only target-tenant terminal ids.
- Customer list contains only target-tenant customer ids.
- Runtime catalog must not contain the sampled foreign product id.

No foreign resource is updated, revoked or deleted during validation.
