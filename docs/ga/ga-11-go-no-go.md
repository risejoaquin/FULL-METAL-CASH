# GA-11 — Go / No-Go

## GO toward GA-12

GA-11 may go to GA-12 only if the real user log contains:

```text
[GA-11] GA-11 PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12
```

Manifest must contain:

```text
phase: GA-11
status: PASS GA CUSTOMER OPERATOR ADMIN ACCEPTANCE / GO GA-12
blockers: {}
schemaVersion: 4
syncContract: schema_version_4
generalAvailabilityActivated: False
```

## NO-GO

GA-11 is blocked if:

- GA-10 PASS evidence is missing;
- build/test/secret scan fails;
- customer/operator/admin required endpoints fail;
- tenant current without auth does not return 401;
- sync contract drifts from schema 4;
- RLS drift is detected;
- duplicate local sales exist;
- pending sync conflicts exist;
- General Availability is activated.

## Known conditions carried forward

- `Concurrency 3+` on the current Railway/upstream path can produce `400 upstream error`.
- `db_waiting_connections_11` was observed in GA-10 and must be monitored.

These conditions do not block GA-11 if the blocker matrix is empty, but they must be resolved or formally accepted before GA-12/public GA launch.
