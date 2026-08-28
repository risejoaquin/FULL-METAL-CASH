# Post-GA-12 Capacity Scale-Up Plan

This path is selected when the team wants to resolve infrastructure limits before expanding launch.

## Triggers

```text
capacity scale-up
Concurrency 3+
Railway
upstream error
database connections
```

## Actions

- Review Railway service limits and proxy/upstream behavior.
- Increase web/API capacity if required.
- Review PostgreSQL pool limits and waiting connections.
- Rerun GA-09 concurrency profile after capacity changes.
- Rerun Post-GA-12 launch decision after evidence is updated.
