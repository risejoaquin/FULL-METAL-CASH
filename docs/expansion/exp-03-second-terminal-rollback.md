# EXP-03 Second Terminal Rollback

## Rollback triggers

- terminal cannot register.
- cash shift cannot close.
- sale missing from read model.
- pending conflict on second terminal.
- dead-letter on second terminal.

## Containment

- stop using the second terminal.
- close any open cash shift for the terminal.
- preserve sale and receipt evidence.
- do not delete production sale data.

## Recovery

- triage terminal status.
- verify cash shift.
- verify audit trail.
- rerun validation after containment.
