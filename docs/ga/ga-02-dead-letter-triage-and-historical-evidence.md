# GA-02 — Dead-letter Triage and Historical Evidence

## Allowed decisions
Each dead-letter must end in exactly one explicit decision: `retry`, `quarantine`, `supersede`, or `close_as_historical_evidence`.

## Automatic closure boundary
GA-02 may automatically choose `close_as_historical_evidence` only when all conditions hold:
1. the row predates the fresh GA-01 baseline;
2. it has `dead_lettered_at`, `error_code`, and `error_message`;
3. it is independently marked as a controlled validation/probe event by terminal fingerprint plus event/payload/error markers;
4. it is not commercial work;
5. no retry or conflict remains executable;
6. an append-only audit decision is written.

If any condition is missing, automatic closure is forbidden and GA-02 fails.

## Retry policy
A commercial or ambiguous `retry_pending` row is never rewritten by this validator. It requires owner, diagnosis, and explicit retry/quarantine/supersede decision.

A controlled over-SLA validation retry can be closed as historical evidence by changing only its queue state to `rejected`, preserving the original inbox row and creating an audit event. No DELETE, ledger rewrite, or business-domain mutation is allowed.
