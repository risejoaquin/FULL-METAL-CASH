# EXP-10 Admin Audit Evidence

## Required evidence
EXP-10 requires audit evidence for:
- `customer.created`
- `customer.updated`
- `user.created`
- `user.updated`

Audit evidence must be tenant scoped and entity scoped. `entity_id` comparisons must be treated safely as text when SQL cross-checks are used.

## Investigation
If evidence is missing, collect:
- PowerShell output from the first `[EXP-10]` line.
- runtime manifest.
- generated log file.
- SQL cross-check output.

## NO-GO
Missing audit evidence for customer or user mutation is a NO-GO for Customer/Admin Management Completion.
