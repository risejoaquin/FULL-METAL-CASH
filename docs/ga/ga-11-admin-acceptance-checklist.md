# GA-11 — Admin Acceptance Checklist

## Admin acceptance

- [ ] Tenant current endpoint validated.
- [ ] Stores endpoint validated.
- [ ] Users endpoint validated.
- [ ] Roles endpoint validated.
- [ ] Permissions endpoint validated.
- [ ] Dashboard endpoint validated.
- [ ] Observability endpoint validated.
- [ ] RLS drift check validated.
- [ ] DB pressure observation from GA-10 is carried forward.
- [ ] General Availability remains not activated.

## Known admin conditions

- GA-09: `Concurrency 3+` may produce `400 upstream error` through current Railway/upstream path.
- GA-10: `db_waiting_connections_11` must be monitored and resolved or formally accepted before GA public launch.
