# SolidPOS — HOTFIX GA-07.3

## Restore Extension Bootstrap Verification

GA-07.2 proved the production snapshot and logical backup are valid. The isolated restore then failed because a restored table uses `public.citext` and the temporary restore database did not expose the required extension at `pg_restore` time.

This hotfix hardens only the isolated DR environment:
- explicit PowerShell parameter binding for restore psql calls;
- `citext` and `pgcrypto` bootstrap before restore;
- immediate verification through `pg_extension`;
- fail-closed behavior if either extension is unavailable.

Production remains untouched. There is no migration, backend change, deployment, release mutation, or GA activation.
